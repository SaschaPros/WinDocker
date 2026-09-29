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

    public Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default) =>
        CallAsync(docker => docker.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = force }, cancellationToken), cancellationToken);

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
