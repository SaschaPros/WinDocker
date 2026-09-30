using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Support;

/// <summary>
/// Hand-written <see cref="IDockerService"/>: serves the configured lists, records every call in
/// <see cref="Calls"/> and lets a test fail or delay individual operations.
/// </summary>
internal sealed class FakeDockerService : IDockerService
{
    public List<ContainerInfo> Containers { get; } = [];

    public List<ImageInfo> Images { get; } = [];

    public List<VolumeInfo> Volumes { get; } = [];

    /// <summary>Every call as text, for example <c>Start c1</c> or <c>ListContainers all=True</c>.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Thrown by list calls while set.</summary>
    public Exception? ListFailure { get; set; }

    /// <summary>Thrown by start, stop and remove calls while set.</summary>
    public Exception? MutationFailure { get; set; }

    /// <summary>Replaces the answer of <see cref="ListContainersAsync"/>, for example to hold it back.</summary>
    public Func<bool, Task<IReadOnlyList<ContainerInfo>>>? ListContainersHandler { get; set; }

    public Func<string, int, bool, CancellationToken, IAsyncEnumerable<LogLine>>? StreamLogsHandler { get; set; }

    public List<(string Id, int Tail, bool Follow)> StreamCalls { get; } = [];

    public int Count(string prefix) => Calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));

    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool all, CancellationToken cancellationToken = default)
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

    public Task StartContainerAsync(string id, CancellationToken cancellationToken = default) => Mutate($"Start {id}");

    public Task StopContainerAsync(string id, CancellationToken cancellationToken = default) => Mutate($"Stop {id}");

    public Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveContainer {id} force={force}", () => Containers.RemoveAll(container => container.Id == id));

    public Task<IReadOnlyList<ImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("ListImages");
        return ListFailure is not null
            ? Task.FromException<IReadOnlyList<ImageInfo>>(ListFailure)
            : Task.FromResult<IReadOnlyList<ImageInfo>>(Images.ToList());
    }

    public Task RemoveImageAsync(string reference, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveImage {reference}", () => Images.RemoveAll(image => image.Reference == reference));

    public Task<IReadOnlyList<VolumeInfo>> ListVolumesAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("ListVolumes");
        return ListFailure is not null
            ? Task.FromException<IReadOnlyList<VolumeInfo>>(ListFailure)
            : Task.FromResult<IReadOnlyList<VolumeInfo>>(Volumes.ToList());
    }

    public Task RemoveVolumeAsync(string name, CancellationToken cancellationToken = default) =>
        Mutate($"RemoveVolume {name}", () => Volumes.RemoveAll(volume => volume.Name == name));

    public IAsyncEnumerable<LogLine> StreamLogsAsync(string id, int tail, bool follow, CancellationToken cancellationToken = default)
    {
        lock (StreamCalls)
        {
            StreamCalls.Add((id, tail, follow));
        }

        return StreamLogsHandler?.Invoke(id, tail, follow, cancellationToken)
            ?? throw new InvalidOperationException("No StreamLogsHandler configured.");
    }

    private Task Mutate(string call, Action? apply = null)
    {
        Calls.Add(call);
        if (MutationFailure is not null)
        {
            return Task.FromException(MutationFailure);
        }

        apply?.Invoke();
        return Task.CompletedTask;
    }
}
