using Docker.DotNet;
using Docker.DotNet.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Integration;

/// <summary>
/// Prunes on the local Docker engine. An unrestricted prune would remove stopped containers, unused images and volumes
/// that belong to somebody else, so these tests only run when the environment variable
/// <c>WINDOCKER_DESTRUCTIVE_TESTS</c> is <c>1</c> (the CI job sets it), and even then every prune goes through the
/// overloads of <see cref="DockerService"/> that add a label filter, with a label that only the test's own resources carry.
/// Skipped as well when no engine answers or when it runs Windows containers.
/// </summary>
[Collection(DockerEngineCollection.Name)]
public class PruneIntegrationTests(DockerEngineFixture engine) : DockerIntegrationTest(engine)
{
    private const string DestructiveTestsVariable = "WINDOCKER_DESTRUCTIVE_TESTS";

    /// <summary>The engine marks the volumes that a container created without a name this way.</summary>
    private const string AnonymousVolumeLabel = "com.docker.volume.anonymous";

    private static void RequireDestructiveTests() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(DestructiveTestsVariable) == "1",
            $"Pruning is only tested with {DestructiveTestsVariable}=1, and then only with a label filter that leaves other resources alone.");

    /// <summary>A label that nothing but the resources of one test carries: the <c>key=value</c> filter, and the labels to put on those resources.</summary>
    private static (string Filter, Dictionary<string, string> Labels) NewLabel()
    {
        var value = Guid.NewGuid().ToString("N");
        return ($"windocker-tests={value}", new Dictionary<string, string> { ["windocker-tests"] = value, [DockerEngineFixture.TestLabel] = "true" });
    }

    private static async Task<bool> ImageExistsAsync(IDockerClient client, string id)
    {
        try
        {
            await client.Images.InspectImageAsync(id, TestToken);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
    }

    private static async Task DeleteImageQuietlyAsync(IDockerClient client, string? id)
    {
        if (id is null)
        {
            return;
        }

        try
        {
            await client.Images.DeleteImageAsync(id, new ImageDeleteParameters { Force = true }, CancellationToken.None);
        }
        catch (DockerApiException)
        {
        }
    }

    private static async Task RemoveVolumeQuietlyAsync(IDockerClient client, string name)
    {
        try
        {
            await client.Volumes.RemoveAsync(name, true, CancellationToken.None);
        }
        catch (DockerApiException)
        {
        }
    }

    [Fact]
    public async Task PruneContainers_RemovesTheStoppedContainersWithTheLabelAndKeepsTheRunningOne()
    {
        RequireDestructiveTests();
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var (filter, labels) = NewLabel();
        var neverStarted = await CreateContainerAsync(client, image, Quiet, start: false, configure: parameters => parameters.Labels = labels);
        var exited = await CreateContainerAsync(client, image, ["true"], configure: parameters => parameters.Labels = labels);
        var running = await CreateContainerAsync(client, image, Quiet, configure: parameters => parameters.Labels = labels);
        try
        {
            using var timeout = TimeoutAfter(TimeSpan.FromSeconds(30));
            await client.Containers.WaitContainerAsync(exited, timeout.Token);

            var result = await service.PruneContainersAsync(filter, TestToken);

            Assert.Equal(2, result.DeletedCount);
            Assert.True(result.SpaceReclaimed >= 0);
            var remaining = await service.ListContainersAsync(all: true, TestToken);
            Assert.DoesNotContain(remaining, container => container.Id == neverStarted || container.Id == exited);
            Assert.Equal("running", Assert.Single(remaining, container => container.Id == running).State);

            var nothingLeft = await service.PruneContainersAsync(filter, TestToken);

            Assert.Equal(0, nothingLeft.DeletedCount);
        }
        finally
        {
            foreach (var id in new[] { neverStarted, exited, running })
            {
                await RemoveQuietlyAsync(client, id);
            }
        }
    }

    [Fact]
    public async Task PruneVolumes_RemovesAnonymousVolumesWithTheLabelAndNamedOnesOnlyWhenAsked()
    {
        RequireDestructiveTests();
        var (client, _) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var (filter, labels) = NewLabel();
        var named = $"windocker-tests-{Guid.NewGuid():N}";
        var anonymous = $"windocker-tests-{Guid.NewGuid():N}";
        try
        {
            await client.Volumes.CreateAsync(new VolumesCreateParameters { Name = named, Labels = labels }, TestToken);
            await client.Volumes.CreateAsync(
                new VolumesCreateParameters { Name = anonymous, Labels = new Dictionary<string, string>(labels) { [AnonymousVolumeLabel] = string.Empty } },
                TestToken);

            var anonymousOnly = await service.PruneVolumesAsync(includeNamed: false, filter, TestToken);

            Assert.Equal(1, anonymousOnly.DeletedCount);
            Assert.True(anonymousOnly.SpaceReclaimed >= 0);
            Assert.Equal([named], (await service.ListVolumesAsync(TestToken)).Select(volume => volume.Name).Where(name => name == named || name == anonymous));

            var withNamed = await service.PruneVolumesAsync(includeNamed: true, filter, TestToken);

            Assert.Equal(1, withNamed.DeletedCount);
            Assert.DoesNotContain(await service.ListVolumesAsync(TestToken), volume => volume.Name == named || volume.Name == anonymous);
        }
        finally
        {
            await RemoveVolumeQuietlyAsync(client, named);
            await RemoveVolumeQuietlyAsync(client, anonymous);
        }
    }

    [Fact]
    public async Task PruneVolumes_KeepsAVolumeThatAContainerStillUses()
    {
        RequireDestructiveTests();
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var (filter, labels) = NewLabel();
        var name = $"windocker-tests-{Guid.NewGuid():N}";
        string? container = null;
        try
        {
            await client.Volumes.CreateAsync(new VolumesCreateParameters { Name = name, Labels = labels }, TestToken);
            container = await CreateContainerAsync(client, image, Quiet, start: false, configure: parameters => parameters.HostConfig = new HostConfig { NetworkMode = "none", Binds = [$"{name}:/data"] });

            var whileInUse = await service.PruneVolumesAsync(includeNamed: true, filter, TestToken);

            Assert.Equal(0, whileInUse.DeletedCount);
            Assert.Contains(await service.ListVolumesAsync(TestToken), volume => volume.Name == name);

            await RemoveQuietlyAsync(client, container);

            var afterwards = await service.PruneVolumesAsync(includeNamed: true, filter, TestToken);

            Assert.Equal(1, afterwards.DeletedCount);
        }
        finally
        {
            if (container is not null)
            {
                await RemoveQuietlyAsync(client, container);
            }

            await RemoveVolumeQuietlyAsync(client, name);
        }
    }

    [Fact]
    public async Task PruneImages_RemovesDanglingImagesWithTheLabelAndTaggedOnesOnlyWhenAllUnusedAreAsked()
    {
        RequireDestructiveTests();
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var (filter, labels) = NewLabel();
        var container = await CreateContainerAsync(client, image, ["true"], start: false);
        string? dangling = null;
        string? tagged = null;
        try
        {
            // Committing gives images that carry the label; the different commands keep them from being one and the same image.
            dangling = (await client.Images.CommitContainerChangesAsync(
                new CommitContainerChangesParameters { ContainerID = container, Cmd = ["echo", "dangling"], Labels = labels },
                TestToken)).ID;
            tagged = (await client.Images.CommitContainerChangesAsync(
                new CommitContainerChangesParameters
                {
                    ContainerID = container,
                    RepositoryName = $"windocker-tests/{Guid.NewGuid():N}",
                    Tag = "v1",
                    Cmd = ["echo", "tagged"],
                    Labels = labels,
                },
                TestToken)).ID;

            var danglingOnly = await service.PruneImagesAsync(allUnused: false, filter, TestToken);

            Assert.True(danglingOnly.DeletedCount >= 1);
            Assert.True(danglingOnly.SpaceReclaimed >= 0);
            Assert.False(await ImageExistsAsync(client, dangling));
            Assert.True(await ImageExistsAsync(client, tagged));

            var allUnused = await service.PruneImagesAsync(allUnused: true, filter, TestToken);

            // The engine reports the removed tag and the deleted image; only the latter counts.
            Assert.Equal(1, allUnused.DeletedCount);
            Assert.False(await ImageExistsAsync(client, tagged));
            Assert.True(await ImageExistsAsync(client, image));
        }
        finally
        {
            await RemoveQuietlyAsync(client, container);
            await DeleteImageQuietlyAsync(client, dangling);
            await DeleteImageQuietlyAsync(client, tagged);
        }
    }
}
