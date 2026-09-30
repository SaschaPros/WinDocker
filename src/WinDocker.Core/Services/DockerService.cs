using System.Buffers;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using WinDocker.Core.Docker;
using WinDocker.Core.Models;
using VolumeInfo = WinDocker.Core.Models.VolumeInfo;

namespace WinDocker.Core.Services;

/// <summary>
/// <see cref="IDockerService"/> on top of Docker.DotNet. The client is created on first use so that an
/// unreachable engine surfaces as <see cref="DockerUnavailableException"/> from the call, not from the constructor.
/// </summary>
public sealed class DockerService : IDockerService, IDisposable
{
    private const int LogReadBufferSize = 16 * 1024;

    private readonly Func<IDockerClient> clientFactory;
    private readonly Lock gate = new();
    private IDockerClient? client;
    private bool disposed;

    /// <summary>Connects like the docker CLI: <c>DOCKER_HOST</c>, then the current docker context, then the platform default.</summary>
    public DockerService()
        : this(DockerClientFactory.CreateDefault)
    {
    }

    /// <param name="clientFactory">Creates the client on first use; the service owns and disposes it.</param>
    public DockerService(Func<IDockerClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        this.clientFactory = clientFactory;
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool all, CancellationToken cancellationToken = default)
    {
        var containers = await CallAsync(
            docker => docker.Containers.ListContainersAsync(new ContainersListParameters { All = all }, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return containers
            .Select(DockerMapper.ToContainerInfo)
            .OrderByDescending(container => container.CreatedAt)
            .ToList();
    }

    public Task StartContainerAsync(string id, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Containers.StartContainerAsync(id, null, cancellationToken), cancellationToken);

    public Task StopContainerAsync(string id, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Containers.StopContainerAsync(id, new ContainerStopParameters(), cancellationToken), cancellationToken);

    public Task RestartContainerAsync(string id, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Containers.RestartContainerAsync(id, new ContainerRestartParameters(), cancellationToken), cancellationToken);

    public Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = force }, cancellationToken), cancellationToken);

    public Task<PruneResult> PruneContainersAsync(CancellationToken cancellationToken = default) =>
        PruneContainersCoreAsync(null, cancellationToken);

    /// <summary>Like <see cref="PruneContainersAsync(CancellationToken)"/>, but only for containers that carry <paramref name="label"/> (<c>key</c> or <c>key=value</c>), so that tests never touch foreign containers.</summary>
    internal Task<PruneResult> PruneContainersAsync(string label, CancellationToken cancellationToken = default) =>
        PruneContainersCoreAsync(label, cancellationToken);

    public async Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        var images = await CallAsync(
            docker => docker.Images.ListImagesAsync(new ImagesListParameters(), cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return images
            .SelectMany(DockerMapper.ToImageInfos)
            .OrderByDescending(image => image.CreatedAt)
            .ThenBy(image => image.Repository, StringComparer.Ordinal)
            .ThenBy(image => image.Tag, StringComparer.Ordinal)
            .ToList();
    }

    public Task RemoveImageAsync(string reference, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Images.DeleteImageAsync(reference, new ImageDeleteParameters(), cancellationToken), cancellationToken);

    public Task<PruneResult> PruneImagesAsync(bool allUnused, CancellationToken cancellationToken = default) =>
        PruneImagesCoreAsync(allUnused, null, cancellationToken);

    /// <summary>Like <see cref="PruneImagesAsync(bool, CancellationToken)"/>, but only for images that carry <paramref name="label"/> (<c>key</c> or <c>key=value</c>), so that tests never touch foreign images.</summary>
    internal Task<PruneResult> PruneImagesAsync(bool allUnused, string label, CancellationToken cancellationToken = default) =>
        PruneImagesCoreAsync(allUnused, label, cancellationToken);

    public async Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            docker => docker.Volumes.ListAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // The engine answers "Volumes": null when there are none.
        return (response.Volumes ?? [])
            .Select(DockerMapper.ToVolumeInfo)
            .OrderBy(volume => volume.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Volumes.RemoveAsync(name, force: false, cancellationToken), cancellationToken);

    public Task<PruneResult> PruneVolumesAsync(bool includeNamed, CancellationToken cancellationToken = default) =>
        PruneVolumesCoreAsync(includeNamed, null, cancellationToken);

    /// <summary>Like <see cref="PruneVolumesAsync(bool, CancellationToken)"/>, but only for volumes that carry <paramref name="label"/> (<c>key</c> or <c>key=value</c>), so that tests never touch foreign volumes.</summary>
    internal Task<PruneResult> PruneVolumesAsync(bool includeNamed, string label, CancellationToken cancellationToken = default) =>
        PruneVolumesCoreAsync(includeNamed, label, cancellationToken);

    public async Task<IReadOnlyList<ComposeProjectInfo>> ListComposeProjectsAsync(CancellationToken cancellationToken = default)
    {
        var containers = await CallAsync(
            docker => docker.Containers.ListContainersAsync(
                new ContainersListParameters { All = true, Filters = Filters(("label", ComposeLabelNames.Project)) },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return ComposeProjects.FromContainers(containers.Select(DockerMapper.ToContainerInfo));
    }

    public async Task RemoveComposeNetworksAsync(string project, CancellationToken cancellationToken = default)
    {
        var networks = await CallAsync(
            docker => docker.Networks.ListNetworksAsync(
                new NetworksListParameters { Filters = Filters(("label", ProjectLabel(project))) },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // An engine that ignores the filter must not make this delete the networks of other projects.
        foreach (var network in networks.Where(network => IsOfProject(network.Labels, project)))
        {
            await CallAsync(docker => docker.Networks.DeleteNetworkAsync(network.ID, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveComposeVolumesAsync(string project, CancellationToken cancellationToken = default)
    {
        var response = await CallAsync(
            docker => docker.Volumes.ListAsync(
                new VolumesListParameters { Filters = Filters(("label", ProjectLabel(project))) },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // The engine answers "Volumes": null when there are none.
        foreach (var volume in (response.Volumes ?? []).Where(volume => IsOfProject(volume.Labels, project)))
        {
            await CallAsync(docker => docker.Volumes.RemoveAsync(volume.Name, force: false, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<LogLine> StreamLogsAsync(
        string id,
        int tail,
        bool follow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = true,
            Follow = follow,
            Tail = tail > 0 ? tail.ToString(CultureInfo.InvariantCulture) : "all",
        };

        // The library inspects the container first, so TTY containers (raw, non-multiplexed streams) work too.
        var stream = await CallAsync(
            docker => docker.Containers.GetContainerLogsAsync(id, parameters, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        using (stream)
        using (cancellationToken.Register(static state => ((MultiplexedStream)state!).Dispose(), stream))
        {
            var splitter = new LogLineSplitter();
            var buffer = ArrayPool<byte>.Shared.Rent(LogReadBufferSize);
            try
            {
                while (true)
                {
                    var read = await ReadAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
                    if (read.EOF)
                    {
                        break;
                    }

                    var isError = read.Target == MultiplexedStream.TargetStream.StandardError;
                    foreach (var line in splitter.Append(buffer.AsSpan(0, read.Count), isError))
                    {
                        yield return line;
                    }
                }

                foreach (var line in splitter.Flush())
                {
                    yield return line;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            client?.Dispose();
            client = null;
        }
    }

    /// <summary>Builds the <c>filters</c> of a list or prune request. Entries without a value are left out; without any entry there is no filter at all.</summary>
    private static Dictionary<string, IDictionary<string, bool>>? Filters(params (string Name, string? Value)[] filters)
    {
        Dictionary<string, IDictionary<string, bool>> result = [];
        foreach (var (name, value) in filters)
        {
            if (value is not null)
            {
                result[name] = new Dictionary<string, bool> { [value] = true };
            }
        }

        return result.Count == 0 ? null : result;
    }

    /// <returns>The value of the <c>label</c> filter that selects what belongs to the compose project.</returns>
    private static string ProjectLabel(string project) => $"{ComposeLabelNames.Project}={project}";

    private static bool IsOfProject(IDictionary<string, string>? labels, string project) =>
        labels is not null && labels.TryGetValue(ComposeLabelNames.Project, out var value) && value == project;

    private static PruneResult ToPruneResult(int deletedCount, ulong spaceReclaimed) => new(deletedCount, long.CreateSaturating(spaceReclaimed));

    private async Task<PruneResult> PruneContainersCoreAsync(string? label, CancellationToken cancellationToken)
    {
        var parameters = new ContainersPruneParameters { Filters = Filters(("label", label)) };
        var response = await CallAsync(
            docker => docker.Containers.PruneContainersAsync(parameters, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // The engine answers null lists when nothing was removed.
        return ToPruneResult(response.ContainersDeleted?.Count ?? 0, response.SpaceReclaimed);
    }

    private async Task<PruneResult> PruneImagesCoreAsync(bool allUnused, string? label, CancellationToken cancellationToken)
    {
        var parameters = new ImagesPruneParameters { Filters = Filters(("dangling", allUnused ? "false" : "true"), ("label", label)) };
        var response = await CallAsync(
            docker => docker.Images.PruneImagesAsync(parameters, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // An entry either deletes an image or only untags one that stays.
        return ToPruneResult(response.ImagesDeleted?.Count(image => !string.IsNullOrEmpty(image.Deleted)) ?? 0, response.SpaceReclaimed);
    }

    private async Task<PruneResult> PruneVolumesCoreAsync(bool includeNamed, string? label, CancellationToken cancellationToken)
    {
        var parameters = new VolumesPruneParameters { Filters = Filters(("all", includeNamed ? "true" : null), ("label", label)) };
        var response = await CallAsync(
            docker => docker.Volumes.PruneAsync(parameters, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return ToPruneResult(response.VolumesDeleted?.Count ?? 0, response.SpaceReclaimed);
    }

    private static async Task<MultiplexedStream.ReadResult> ReadAsync(MultiplexedStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelling disposes the stream, which surfaces as ObjectDisposedException or IOException.
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception exception) when (TryTranslate(exception, cancellationToken, out var translated))
        {
            throw translated;
        }
    }

    private async Task<T> CallAsync<T>(Func<IDockerClient, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call(GetClient()).ConfigureAwait(false);
        }
        catch (Exception exception) when (TryTranslate(exception, cancellationToken, out var translated))
        {
            throw translated;
        }
    }

    private async Task CallAsync(Func<IDockerClient, Task> call, CancellationToken cancellationToken)
    {
        try
        {
            await call(GetClient()).ConfigureAwait(false);
        }
        catch (Exception exception) when (TryTranslate(exception, cancellationToken, out var translated))
        {
            throw translated;
        }
    }

    private IDockerClient GetClient()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (client is null)
            {
                try
                {
                    client = clientFactory();
                }
                catch (Exception exception)
                {
                    // Invalid DOCKER_HOST, unknown docker context, unsupported endpoint: nothing to connect to.
                    throw new DockerUnavailableException(exception.Message, exception);
                }
            }

            return client;
        }
    }

    /// <summary>
    /// Maps what the client throws when the engine cannot be reached. Errors the engine itself reports
    /// (<see cref="DockerApiException"/>) and the caller's own cancellation are not translated.
    /// </summary>
    private static bool TryTranslate(Exception exception, CancellationToken cancellationToken, [NotNullWhen(true)] out Exception? translated)
    {
        translated = exception switch
        {
            DockerUnavailableException or DockerApiException => null,
            OperationCanceledException when cancellationToken.IsCancellationRequested => null,
            // The request timeout of the client elapsed.
            OperationCanceledException => new DockerUnavailableException(exception.Message, exception),
            _ when IsConnectivityFailure(exception) => new DockerUnavailableException(DescribeFailure(exception), exception),
            _ => null,
        };

        return translated is not null;
    }

    private static bool IsConnectivityFailure(Exception exception) => exception switch
    {
        HttpRequestException or IOException or SocketException or TimeoutException or UnauthorizedAccessException or Win32Exception
            or DockerConfigurationException or SshDockerEndpointNotSupportedException => true,

        // A refused TCP connection is reported as an AggregateException with one SocketException per address.
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsConnectivityFailure),
        _ => false,
    };

    private static string DescribeFailure(Exception exception) =>
        exception is AggregateException { InnerException: { } inner } ? inner.Message : exception.Message;
}
