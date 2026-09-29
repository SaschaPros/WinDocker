using WinDocker.Core.Models;

namespace WinDocker.Core.Services;

/// <summary>
/// Access to the local Docker engine. Implementations throw <see cref="DockerUnavailableException"/> when the
/// engine cannot be reached and let the engine's own API errors (<c>DockerApiException</c>) pass through.
/// </summary>
public interface IDockerService
{
    /// <summary>Lists containers newest first; <paramref name="all"/> includes stopped ones, like <c>docker ps -a</c>.</summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool all, CancellationToken cancellationToken = default);

    Task StartContainerAsync(string id, CancellationToken cancellationToken = default);

    Task StopContainerAsync(string id, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default);

    /// <summary>Lists images newest first, one row per repository:tag like <c>docker images</c>.</summary>
    Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes an image by <see cref="ImageInfo.Reference"/> with <c>docker rmi</c> semantics.</summary>
    Task RemoveImageAsync(string reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the stdout and stderr lines of a container. Returns the last <paramref name="tail"/> lines
    /// (all lines when it is zero or negative) and, with <paramref name="follow"/>, keeps streaming until the
    /// container stops or the token is cancelled.
    /// </summary>
    IAsyncEnumerable<LogLine> StreamLogsAsync(string id, int tail, bool follow, CancellationToken cancellationToken = default);
}
