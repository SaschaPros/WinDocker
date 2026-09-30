using WinDocker.Core.Docker;
using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Support;

/// <summary>
/// Hand-written <see cref="IDockerService"/>: serves the configured lists, records every call in
/// <see cref="Calls"/> and lets a test fail or delay individual operations. Safe to call from several threads
/// (bulk actions run in parallel); the test itself must not touch the lists while calls are in flight.
/// </summary>
internal sealed class FakeDockerService : IDockerService
{
    private readonly Lock gate = new();

    /// <summary>The containers of the engine. Those with <see cref="ContainerInfo.Compose"/> labels make up the projects that <see cref="ListComposeProjectsAsync"/> answers.</summary>
    public List<ContainerInfo> Containers { get; } = [];

    public List<ImageInfo> Images { get; } = [];

    public List<VolumeInfo> Volumes { get; } = [];

    /// <summary>What the prune calls answer.</summary>
    public PruneResult PruneResult { get; set; } = new(0, 0);

    /// <summary>Every call as text, for example <c>Start c1</c> or <c>ListContainers all=True</c>. The order of parallel calls is not defined.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Thrown by list calls while set.</summary>
    public Exception? ListFailure { get; set; }

    /// <summary>Thrown by start, stop, restart, remove and prune calls, and by the removal of compose networks and volumes, while set.</summary>
    public Exception? MutationFailure { get; set; }

    /// <summary>
    /// Failures of single calls. The key is the whole call as it appears in <see cref="Calls"/> (<c>RemoveComposeVolumes shop</c>)
    /// or what the call names: the container ID, the image reference, the volume name or the compose project.
    /// Takes precedence over <see cref="MutationFailure"/>.
    /// </summary>
    public Dictionary<string, Exception> MutationFailures { get; } = [];

    /// <summary>Replaces the answer of <see cref="ListContainersAsync"/>, for example to hold it back.</summary>
    public Func<bool, Task<IReadOnlyList<ContainerInfo>>>? ListContainersHandler { get; set; }

    /// <summary>Replaces the answer of <see cref="ListComposeProjectsAsync"/>, for example to hold it back.</summary>
    public Func<Task<IReadOnlyList<ComposeProjectInfo>>>? ListComposeProjectsHandler { get; set; }

    public Func<string, int, bool, CancellationToken, IAsyncEnumerable<LogLine>>? StreamLogsHandler { get; set; }

    public List<(string Id, int Tail, bool Follow)> StreamCalls { get; } = [];

    public int Count(string prefix)
    {
        lock (gate)
        {
            return Calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool all, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Calls.Add($"ListContainers all={all}");
            if (ListFailure is not null)
            {
                return Task.FromException<IReadOnlyList<ContainerInfo>>(ListFailure);
            }

            if (ListContainersHandler is not null)
            {
                return ListContainersHandler(all);
            }

            IReadOnlyList<ContainerInfo> snapshot = Containers.Where(container => all || container.State == "running").ToList();
            return Task.FromResult(snapshot);
        }
    }

    public Task StartContainerAsync(string id, CancellationToken cancellationToken = default) => Mutate($"Start {id}", id);

    public Task StopContainerAsync(string id, CancellationToken cancellationToken = default) => Mutate($"Stop {id}", id);

    public Task RestartContainerAsync(string id, CancellationToken cancellationToken = default) => Mutate($"Restart {id}", id);

    public Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveContainer {id} force={force}", id, () => Containers.RemoveAll(container => container.Id == id));

    public Task<PruneResult> PruneContainersAsync(CancellationToken cancellationToken = default) => Prune("PruneContainers");

    public Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Calls.Add("ListImages");
            return ListFailure is not null
                ? Task.FromException<IReadOnlyList<ImageInfo>>(ListFailure)
                : Task.FromResult<IReadOnlyList<ImageInfo>>(Images.ToList());
        }
    }

    public Task RemoveImageAsync(string reference, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveImage {reference}", reference, () => Images.RemoveAll(image => image.Reference == reference));

    public Task<PruneResult> PruneImagesAsync(bool allUnused, CancellationToken cancellationToken = default) =>
        Prune($"PruneImages all={allUnused}");

    public Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Calls.Add("ListVolumes");
            return ListFailure is not null
                ? Task.FromException<IReadOnlyList<VolumeInfo>>(ListFailure)
                : Task.FromResult<IReadOnlyList<VolumeInfo>>(Volumes.ToList());
        }
    }

    public Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveVolume {name}", name, () => Volumes.RemoveAll(volume => volume.Name == name));

    public Task<PruneResult> PruneVolumesAsync(bool includeNamed, CancellationToken cancellationToken = default) =>
        Prune($"PruneVolumes includeNamed={includeNamed}");

    public Task<IReadOnlyList<ComposeProjectInfo>> ListComposeProjectsAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Calls.Add("ListComposeProjects");
            if (ListFailure is not null)
            {
                return Task.FromException<IReadOnlyList<ComposeProjectInfo>>(ListFailure);
            }

            return ListComposeProjectsHandler is not null
                ? ListComposeProjectsHandler()
                : Task.FromResult(ComposeProjects.FromContainers(Containers));
        }
    }

    public Task RemoveComposeNetworksAsync(string project, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveComposeNetworks {project}", project);

    public Task RemoveComposeVolumesAsync(string project, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveComposeVolumes {project}", project);

    public IAsyncEnumerable<LogLine> StreamLogsAsync(string id, int tail, bool follow, CancellationToken cancellationToken = default)
    {
        lock (StreamCalls)
        {
            StreamCalls.Add((id, tail, follow));
        }

        return StreamLogsHandler?.Invoke(id, tail, follow, cancellationToken)
            ?? throw new InvalidOperationException("No StreamLogsHandler configured.");
    }

    private Task Mutate(string call, string target, Action? apply = null)
    {
        lock (gate)
        {
            Calls.Add(call);
            var failure = FailureOf(call, target);
            if (failure is not null)
            {
                return Task.FromException(failure);
            }

            apply?.Invoke();
            return Task.CompletedTask;
        }
    }

    private Task<PruneResult> Prune(string call)
    {
        lock (gate)
        {
            Calls.Add(call);
            var failure = FailureOf(call, call);
            return failure is not null ? Task.FromException<PruneResult>(failure) : Task.FromResult(PruneResult);
        }
    }

    private Exception? FailureOf(string call, string target) =>
        MutationFailures.GetValueOrDefault(call) ?? MutationFailures.GetValueOrDefault(target) ?? MutationFailure;
}
