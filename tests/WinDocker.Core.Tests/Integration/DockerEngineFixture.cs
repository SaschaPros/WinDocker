using Docker.DotNet;
using Docker.DotNet.Models;

namespace WinDocker.Core.Tests.Integration;

/// <summary>
/// Finds out once whether a Docker engine answers a ping, and hands out a raw client to prepare test data.
/// Every container, network, volume and image this test project creates carries <see cref="TestLabel"/> so leftovers can be cleaned up.
/// </summary>
public sealed class DockerEngineFixture : IAsyncLifetime
{
    public const string TestLabel = "windocker.tests";

    /// <summary>Images that print and sleep, in order of preference. MCR has no pull rate limits.</summary>
    private static readonly (string Repository, string Tag)[] ImageCandidates =
    [
        ("mcr.microsoft.com/azurelinux/busybox", "1.36"),
        ("alpine", "3"),
    ];

    private readonly SemaphoreSlim imageGate = new(1, 1);
    private string? image;

    public IDockerClient? Client { get; private set; }

    /// <summary>Why the tests are skipped; null when an engine answered.</summary>
    public string? UnavailableReason { get; private set; }

    public bool IsLinuxEngine { get; private set; }

    public async ValueTask InitializeAsync()
    {
        DockerClient? client = null;
        try
        {
            client = new DockerClientBuilder().Build();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await client.System.PingAsync(timeout.Token);
            var info = await client.System.GetSystemInfoAsync(timeout.Token);

            IsLinuxEngine = string.Equals(info.OSType, "linux", StringComparison.OrdinalIgnoreCase);
            Client = client;
        }
        catch (Exception exception)
        {
            client?.Dispose();
            UnavailableReason = $"No Docker engine answered a ping ({exception.GetType().Name}: {exception.Message})";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null)
        {
            await RemoveLeftoversAsync();
            Client.Dispose();
        }

        imageGate.Dispose();
    }

    /// <summary>Returns a small image with a shell, pulling it on first use; null when none can be pulled.</summary>
    public async Task<string?> EnsureImageAsync(CancellationToken cancellationToken)
    {
        await imageGate.WaitAsync(cancellationToken);
        try
        {
            return image ??= await FindOrPullImageAsync(cancellationToken);
        }
        finally
        {
            imageGate.Release();
        }
    }

    private async Task<string?> FindOrPullImageAsync(CancellationToken cancellationToken)
    {
        foreach (var (repository, tag) in ImageCandidates)
        {
            var reference = $"{repository}:{tag}";
            try
            {
                await Client!.Images.InspectImageAsync(reference, cancellationToken);
                return reference;
            }
            catch (DockerImageNotFoundException)
            {
            }

            try
            {
                await Client!.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = repository, Tag = tag },
                    null,
                    new Progress<JSONMessage>(),
                    cancellationToken);
                return reference;
            }
            catch (Exception exception) when (exception is DockerApiException or HttpRequestException or IOException)
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private async Task RemoveLeftoversAsync()
    {
        var label = new Dictionary<string, IDictionary<string, bool>> { ["label"] = new Dictionary<string, bool> { [TestLabel] = true } };

        // Containers first: they hold on to the networks, volumes and images.
        await RemoveQuietlyAsync(async () =>
        {
            var containers = await Client!.Containers.ListContainersAsync(new ContainersListParameters { All = true, Filters = label });
            foreach (var container in containers)
            {
                await Client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true });
            }
        });

        await RemoveQuietlyAsync(async () =>
        {
            var networks = await Client!.Networks.ListNetworksAsync(new NetworksListParameters { Filters = label });
            foreach (var network in networks)
            {
                await Client.Networks.DeleteNetworkAsync(network.ID);
            }
        });

        await RemoveQuietlyAsync(async () =>
        {
            var volumes = await Client!.Volumes.ListAsync(new VolumesListParameters { Filters = label });
            foreach (var volume in volumes.Volumes ?? [])
            {
                await Client.Volumes.RemoveAsync(volume.Name, force: true);
            }
        });

        await RemoveQuietlyAsync(async () =>
        {
            var images = await Client!.Images.ListImagesAsync(new ImagesListParameters { Filters = label });
            foreach (var image in images)
            {
                await Client.Images.DeleteImageAsync(image.ID, new ImageDeleteParameters { Force = true });
            }
        });
    }

    /// <summary>Every kind of leftover is removed on a best effort basis: a failure must not keep the others from being cleaned up.</summary>
    private static async Task RemoveQuietlyAsync(Func<Task> remove)
    {
        try
        {
            await remove();
        }
        catch (Exception)
        {
        }
    }
}
