using Docker.DotNet;
using Docker.DotNet.Models;
using WinDocker.Core.Docker;
using WinDocker.Core.Columns;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.Integration;

/// <summary>
/// Lists and manages compose projects on the local Docker engine. The engine only knows the labels that docker compose puts
/// on the containers, networks and volumes of a project, so the tests set up projects by creating resources with those labels.
/// Skipped when no engine answers or when it runs Windows containers.
/// </summary>
[Collection(DockerEngineCollection.Name)]
public class ComposeIntegrationTests(DockerEngineFixture engine) : DockerIntegrationTest(engine)
{
    private sealed record TestProject(string Name, string Network, string? Volume, IReadOnlyList<string> ContainerIds);

    private static Dictionary<string, string> ProjectLabels(string project) => new()
    {
        [ComposeLabelNames.Project] = project,
        [DockerEngineFixture.TestLabel] = "true",
    };

    private static Dictionary<string, IDictionary<string, bool>> ProjectFilter(string project) =>
        new() { ["label"] = new Dictionary<string, bool> { [$"{ComposeLabelNames.Project}={project}"] = true } };

    private static Task<string> CreateServiceContainerAsync(
        IDockerClient client,
        string image,
        string project,
        string service,
        string network,
        string? volume = null,
        bool oneOff = false,
        bool start = true) =>
        CreateContainerAsync(client, image, Quiet, start: start, configure: parameters =>
        {
            parameters.Name = oneOff ? $"{project}-{service}-run-1" : $"{project}-{service}-1";
            parameters.Labels[ComposeLabelNames.Project] = project;
            parameters.Labels[ComposeLabelNames.Service] = service;
            parameters.Labels[ComposeLabelNames.WorkingDir] = $"/srv/{project}";
            parameters.Labels[ComposeLabelNames.ConfigFiles] = $"/srv/{project}/compose.yaml";
            parameters.Labels[ComposeLabelNames.OneOff] = oneOff ? "True" : "False";
            parameters.HostConfig = new HostConfig { NetworkMode = network };
            if (volume is not null)
            {
                parameters.HostConfig.Binds = [$"{volume}:/data"];
            }
        });

    /// <summary>The names of a project that does not exist yet, unique for this test run.</summary>
    private static TestProject DeclareProject(bool withVolume)
    {
        var name = $"windocker-tests-{Guid.NewGuid():N}";
        return new TestProject(name, $"{name}_default", withVolume ? $"{name}_data" : null, []);
    }

    private static async Task CreateNetworkAndVolumeAsync(IDockerClient client, TestProject project)
    {
        await client.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = project.Network, Labels = ProjectLabels(project.Name) }, TestToken);
        if (project.Volume is not null)
        {
            await client.Volumes.CreateAsync(new VolumesCreateParameters { Name = project.Volume, Labels = ProjectLabels(project.Name) }, TestToken);
        }
    }

    /// <summary>A project like <c>docker compose up</c> leaves it: a network, optionally a volume, and the running services db and web.</summary>
    private static async Task<TestProject> CreateProjectAsync(IDockerClient client, string image, bool withVolume = false)
    {
        var project = DeclareProject(withVolume);
        await CreateNetworkAndVolumeAsync(client, project);
        var db = await CreateServiceContainerAsync(client, image, project.Name, "db", project.Network, project.Volume);
        var web = await CreateServiceContainerAsync(client, image, project.Name, "web", project.Network);
        return project with { ContainerIds = [db, web] };
    }

    private static async Task RemoveProjectQuietlyAsync(IDockerClient client, TestProject project)
    {
        foreach (var id in project.ContainerIds)
        {
            await RemoveQuietlyAsync(client, id);
        }

        try
        {
            await client.Networks.DeleteNetworkAsync(project.Network, CancellationToken.None);
        }
        catch (DockerApiException)
        {
        }

        if (project.Volume is not null)
        {
            try
            {
                await client.Volumes.RemoveAsync(project.Volume, true, CancellationToken.None);
            }
            catch (DockerApiException)
            {
            }
        }
    }

    private static ComposeViewModel CreateViewModel(DockerService service, FakeDialogService dialogs)
    {
        var settings = FakeSettingsStore.CreateService();
        return new(service, dialogs, new FakeLocalizer(), settings, new ListLayouts(settings), TimeProvider.System);
    }

    /// <summary>Reloads the projects until <paramref name="item"/> shows the expected number of running containers; the engine's container list can trail a stop.</summary>
    private static Task WaitForRunningAsync(ComposeViewModel viewModel, ComposeProjectItem item, int running) =>
        WaitUntilAsync(
            async () =>
            {
                await viewModel.RefreshCommand.ExecuteAsync(null);
                return item.Info.RunningCount == running;
            },
            $"{running} running containers in the project",
            TimeSpan.FromSeconds(20));

    private static async Task<string[]> StartedAtAsync(IDockerClient client, IEnumerable<string> ids)
    {
        var startedAt = new List<string>();
        foreach (var id in ids)
        {
            startedAt.Add((await client.Containers.InspectContainerAsync(id, TestToken)).State!.StartedAt);
        }

        return [.. startedAt];
    }

    [Fact]
    public async Task ListComposeProjects_FindsAProjectWithItsServicesCountsAndLabels()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var project = await CreateProjectAsync(client, image);
        try
        {
            var oneOff = await CreateServiceContainerAsync(client, image, project.Name, "web", project.Network, oneOff: true, start: false);

            var found = Assert.Single(await service.ListComposeProjectsAsync(TestToken), candidate => candidate.Name == project.Name);

            Assert.Equal(["db", "web"], found.Services);
            Assert.Equal("db, web", found.ServicesText);
            Assert.Equal(2, found.RunningCount);
            Assert.Equal(3, found.TotalCount);
            Assert.Equal($"/srv/{project.Name}", found.WorkingDir);
            Assert.Equal($"/srv/{project.Name}/compose.yaml", found.ConfigFiles);
            AssertRecent(found.CreatedAt);
            Assert.Equal(found.Containers.Select(container => container.CreatedAt).Order(), found.Containers.Select(container => container.CreatedAt));
            Assert.Equal(
                [(project.ContainerIds[0], "db", "running", false), (project.ContainerIds[1], "web", "running", false), (oneOff, "web", "created", true)],
                found.Containers.Select(container => (container.Id, container.Service, container.State, container.IsOneOff)).OrderBy(container => container.Item2).ThenBy(container => container.Item4));
            Assert.All(found.Containers, container => Assert.StartsWith(project.Name, container.Name));

            var containers = await service.ListContainersAsync(all: true, TestToken);
            var web = Assert.Single(containers, container => container.Id == project.ContainerIds[1]);
            Assert.Equal($"{project.Name} / web", web.ProjectText);
            Assert.False(web.Compose!.IsOneOff);
        }
        finally
        {
            await RemoveProjectQuietlyAsync(client, project);
        }
    }

    [Fact]
    public async Task ListComposeProjects_DoesNotListContainersOfNoProject()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Quiet);
        try
        {
            var projects = await service.ListComposeProjectsAsync(TestToken);

            Assert.DoesNotContain(projects, project => project.Containers.Any(container => container.Id == id));
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public Task ComposeViewModel_StopsStartsRestartsAndRemovesAProject() => UiThread.RunAsync(async () =>
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var project = await CreateProjectAsync(client, image);
        try
        {
            var dialogs = new FakeDialogService();
            var viewModel = CreateViewModel(service, dialogs);
            await viewModel.RefreshCommand.ExecuteAsync(null);
            var item = Assert.Single(viewModel.Projects, candidate => candidate.Name == project.Name);
            Assert.Equal($"{ResourceKeys.ComposeStatusRunning}|2|2", item.StatusText);
            viewModel.UpdateSelection([item]);
            Assert.True(viewModel.StopCommand.CanExecute(null));
            Assert.False(viewModel.StartCommand.CanExecute(null));

            await viewModel.StopCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasError, viewModel.ErrorMessage);
            await WaitForRunningAsync(viewModel, item, running: 0);

            Assert.Same(item, Assert.Single(viewModel.Projects, candidate => candidate.Name == project.Name));
            Assert.Equal(ResourceKeys.ComposeStatusExited, item.StatusText);
            Assert.Same(item, Assert.Single(viewModel.SelectedProjects));
            Assert.True(viewModel.StartCommand.CanExecute(null));
            Assert.False(viewModel.StopCommand.CanExecute(null));

            await viewModel.StartCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasError, viewModel.ErrorMessage);
            await WaitForRunningAsync(viewModel, item, running: 2);

            Assert.Equal($"{ResourceKeys.ComposeStatusRunning}|2|2", item.StatusText);

            var startedBefore = await StartedAtAsync(client, project.ContainerIds);
            await viewModel.RestartCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasError, viewModel.ErrorMessage);
            await WaitForRunningAsync(viewModel, item, running: 2);
            var startedAfter = await StartedAtAsync(client, project.ContainerIds);

            Assert.All(startedBefore.Zip(startedAfter), started => Assert.NotEqual(started.First, started.Second));

            await viewModel.DownCommand.ExecuteAsync(null);

            var request = Assert.Single(dialogs.Requests);
            Assert.Equal($"{ResourceKeys.ConfirmComposeDownMessage}|{project.Name}", request.Message);
            Assert.Equal(ResourceKeys.ConfirmComposeDownVolumesOption, request.OptionText);
            Assert.False(viewModel.HasError);
            Assert.DoesNotContain(viewModel.Projects, candidate => candidate.Name == project.Name);
            Assert.Empty(viewModel.SelectedProjects);
            Assert.DoesNotContain(await service.ListComposeProjectsAsync(TestToken), candidate => candidate.Name == project.Name);
            Assert.Empty(await client.Networks.ListNetworksAsync(new NetworksListParameters { Filters = ProjectFilter(project.Name) }, TestToken));
        }
        finally
        {
            await RemoveProjectQuietlyAsync(client, project);
        }
    });

    [Fact]
    public Task ComposeViewModel_LeavesOneOffContainersAloneExceptWhenRemovingTheProject() => UiThread.RunAsync(async () =>
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var project = await CreateProjectAsync(client, image);
        var oneOff = await CreateServiceContainerAsync(client, image, project.Name, "web", project.Network, oneOff: true, start: false);
        project = project with { ContainerIds = [.. project.ContainerIds, oneOff] };
        try
        {
            var viewModel = CreateViewModel(service, new FakeDialogService());
            await viewModel.RefreshCommand.ExecuteAsync(null);
            var item = Assert.Single(viewModel.Projects, candidate => candidate.Name == project.Name);
            viewModel.UpdateSelection([item]);

            await viewModel.StopCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasError, viewModel.ErrorMessage);
            await WaitForRunningAsync(viewModel, item, running: 0);
            await viewModel.StartCommand.ExecuteAsync(null);
            Assert.False(viewModel.HasError, viewModel.ErrorMessage);
            await WaitForRunningAsync(viewModel, item, running: 2);

            Assert.Equal($"{ResourceKeys.ComposeStatusPartial}|2|3", item.StatusText);
            Assert.Equal("created", (await client.Containers.InspectContainerAsync(oneOff, TestToken)).State!.Status);

            await viewModel.DownCommand.ExecuteAsync(null);

            Assert.False(viewModel.HasError, viewModel.ErrorMessage);
            await Assert.ThrowsAsync<DockerContainerNotFoundException>(() => client.Containers.InspectContainerAsync(oneOff, TestToken));
            Assert.DoesNotContain(await service.ListComposeProjectsAsync(TestToken), candidate => candidate.Name == project.Name);
        }
        finally
        {
            await RemoveProjectQuietlyAsync(client, project);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Down_RemovesTheVolumesOfTheProjectOnlyWhenAsked(bool removeVolumes) => UiThread.RunAsync(async () =>
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var project = await CreateProjectAsync(client, image, withVolume: true);
        try
        {
            var dialogs = new FakeDialogService();
            dialogs.Script(new ConfirmResult(true, removeVolumes));
            var viewModel = CreateViewModel(service, dialogs);
            await viewModel.RefreshCommand.ExecuteAsync(null);
            viewModel.UpdateSelection([Assert.Single(viewModel.Projects, candidate => candidate.Name == project.Name)]);

            await viewModel.DownCommand.ExecuteAsync(null);

            Assert.False(viewModel.HasError);
            Assert.DoesNotContain(await service.ListComposeProjectsAsync(TestToken), candidate => candidate.Name == project.Name);
            var volumes = await client.Volumes.ListAsync(new VolumesListParameters { Filters = ProjectFilter(project.Name) }, TestToken);
            Assert.Equal(removeVolumes ? 0 : 1, (volumes.Volumes ?? []).Count);
        }
        finally
        {
            await RemoveProjectQuietlyAsync(client, project);
        }
    });

    [Fact]
    public async Task RemovingNetworksAndVolumes_OnlyTouchesTheGivenProject()
    {
        var (client, _) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var target = DeclareProject(withVolume: true);
        var bystander = DeclareProject(withVolume: true);
        try
        {
            await CreateNetworkAndVolumeAsync(client, target);
            await CreateNetworkAndVolumeAsync(client, bystander);

            await service.RemoveComposeNetworksAsync(target.Name, TestToken);

            Assert.Empty(await client.Networks.ListNetworksAsync(new NetworksListParameters { Filters = ProjectFilter(target.Name) }, TestToken));
            Assert.Single(await client.Networks.ListNetworksAsync(new NetworksListParameters { Filters = ProjectFilter(bystander.Name) }, TestToken));
            Assert.Single((await client.Volumes.ListAsync(new VolumesListParameters { Filters = ProjectFilter(target.Name) }, TestToken)).Volumes);

            await service.RemoveComposeVolumesAsync(target.Name, TestToken);

            Assert.Empty((await client.Volumes.ListAsync(new VolumesListParameters { Filters = ProjectFilter(target.Name) }, TestToken)).Volumes ?? []);
            Assert.Single((await client.Volumes.ListAsync(new VolumesListParameters { Filters = ProjectFilter(bystander.Name) }, TestToken)).Volumes);
            Assert.Single(await client.Networks.ListNetworksAsync(new NetworksListParameters { Filters = ProjectFilter(bystander.Name) }, TestToken));

            // Nothing left to remove is no error (for volumes the engine answers "Volumes": null).
            await service.RemoveComposeNetworksAsync(target.Name, TestToken);
            await service.RemoveComposeVolumesAsync(target.Name, TestToken);
        }
        finally
        {
            await RemoveProjectQuietlyAsync(client, target);
            await RemoveProjectQuietlyAsync(client, bystander);
        }
    }

    [Fact]
    public async Task RemovingTheNetworkOfAProjectThatStillHasContainers_ThrowsTheEnginesError()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var project = await CreateProjectAsync(client, image);
        try
        {
            var exception = await Assert.ThrowsAnyAsync<DockerApiException>(() => service.RemoveComposeNetworksAsync(project.Name, TestToken));

            Assert.Contains("active endpoints", DockerFormat.DaemonMessage(exception.ResponseBody));
        }
        finally
        {
            await RemoveProjectQuietlyAsync(client, project);
        }
    }

    [Fact]
    public async Task RestartContainer_StartsTheContainerAgain()
    {
        var (client, image) = await RequireLinuxEngineAsync();
        using var service = new DockerService();
        var id = await CreateContainerAsync(client, image, Quiet);
        try
        {
            var before = await StartedAtAsync(client, [id]);

            await service.RestartContainerAsync(id, TestToken);

            Assert.NotEqual(before, await StartedAtAsync(client, [id]));
            Assert.Equal("running", Assert.Single(await service.ListContainersAsync(all: true, TestToken), container => container.Id == id).State);
        }
        finally
        {
            await RemoveQuietlyAsync(client, id);
        }
    }

    [Fact]
    public async Task RestartingAContainerThatDoesNotExist_ThrowsTheEnginesError()
    {
        RequireEngine();
        using var service = new DockerService();

        var exception = await Assert.ThrowsAnyAsync<DockerApiException>(() => service.RestartContainerAsync("windocker-tests-missing", TestToken));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }
}
