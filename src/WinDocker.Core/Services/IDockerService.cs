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

    Task RestartContainerAsync(string id, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default);

    /// <summary>Removes all stopped containers, like <c>docker container prune</c>.</summary>
    Task<PruneResult> PruneContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists images newest first, one row per repository:tag like <c>docker images</c>.</summary>
    Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes an image by <see cref="ImageInfo.Reference"/> with <c>docker rmi</c> semantics.</summary>
    Task RemoveImageAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the images that no container uses: only the dangling ones (no tag) like <c>docker image prune</c>, or with
    /// <paramref name="allUnused"/> every unused image like <c>docker image prune -a</c>. The count includes only images
    /// that were deleted, not tags that were merely removed from an image that stays.
    /// </summary>
    Task<PruneResult> PruneImagesAsync(bool allUnused, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(CancellationToken cancellationToken = default);

    Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the volumes that no container uses: only the anonymous ones like <c>docker volume prune</c>, or with
    /// <paramref name="includeNamed"/> the named ones as well like <c>docker volume prune -a</c>. Sorting out the anonymous
    /// volumes needs API 1.42 (Docker 23.0); older engines prune named volumes regardless of <paramref name="includeNamed"/>.
    /// </summary>
    Task<PruneResult> PruneVolumesAsync(bool includeNamed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the compose projects that have containers, stopped ones and one-off containers included, ordered by project name.
    /// The engine knows no such thing as a project: it is derived from the <c>com.docker.compose.project</c> label of the containers.
    /// </summary>
    Task<IReadOnlyList<ComposeProjectInfo>> ListComposeProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the networks that compose created for <paramref name="project"/> (labeled <c>com.docker.compose.project</c>), like the network part of <c>docker compose down</c>. The containers must be gone.</summary>
    Task RemoveComposeNetworksAsync(string project, CancellationToken cancellationToken = default);

    /// <summary>Removes the volumes that compose created for <paramref name="project"/> and their data, like <c>docker compose down --volumes</c>. The containers must be gone.</summary>
    Task RemoveComposeVolumesAsync(string project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the stdout and stderr lines of a container. Returns the last <paramref name="tail"/> lines
    /// (all lines when it is zero or negative) and, with <paramref name="follow"/>, keeps streaming until the
    /// container stops or the token is cancelled.
    /// </summary>
    IAsyncEnumerable<LogLine> StreamLogsAsync(string id, int tail, bool follow, CancellationToken cancellationToken = default);
}
