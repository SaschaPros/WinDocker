namespace WinDocker.Core.Models;

/// <summary>
/// A compose project as the engine shows it: the containers that carry its project label, one-off containers included.
/// Two instances are equal when everything, including the contents of the lists, is equal, so an unchanged project
/// does not count as a change on the next refresh.
/// </summary>
/// <param name="Name">Name of the project.</param>
/// <param name="WorkingDir">Directory the project was started from, or <see langword="null"/> when its containers are not labeled with it.</param>
/// <param name="ConfigFiles">The compose files as the label lists them (comma separated), or <see langword="null"/>.</param>
/// <param name="Services">The names of the services that have a container, without repeats and in alphabetical order.</param>
/// <param name="RunningCount">Number of containers in state "running".</param>
/// <param name="TotalCount">Number of containers, running or not.</param>
/// <param name="Containers">The containers of the project, oldest first.</param>
/// <param name="CreatedAt">Creation time of the oldest container.</param>
public sealed record ComposeProjectInfo(
    string Name,
    string? WorkingDir,
    string? ConfigFiles,
    IReadOnlyList<string> Services,
    int RunningCount,
    int TotalCount,
    IReadOnlyList<ComposeContainer> Containers,
    DateTimeOffset CreatedAt)
{
    public string ServicesText => string.Join(", ", Services);

    /// <summary>True when the project has a service container that can be started. One-off containers do not count, see <see cref="ComposeContainer.IsOneOff"/>.</summary>
    public bool CanStart => Containers.Any(container => container is { IsOneOff: false, CanStart: true });

    /// <summary>True when the project has a service container that can be stopped. One-off containers do not count.</summary>
    public bool CanStop => Containers.Any(container => container is { IsOneOff: false, CanStop: true });

    /// <summary>True when the project has at least one container that belongs to a service, not just one-off containers.</summary>
    public bool HasServiceContainers => Containers.Any(container => !container.IsOneOff);

    public bool Equals(ComposeProjectInfo? other) =>
        other is not null
        && Name == other.Name
        && WorkingDir == other.WorkingDir
        && ConfigFiles == other.ConfigFiles
        && RunningCount == other.RunningCount
        && TotalCount == other.TotalCount
        && CreatedAt == other.CreatedAt
        && Services.SequenceEqual(other.Services)
        && Containers.SequenceEqual(other.Containers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(WorkingDir);
        hash.Add(ConfigFiles);
        hash.Add(RunningCount);
        hash.Add(TotalCount);
        hash.Add(CreatedAt);
        foreach (var service in Services)
        {
            hash.Add(service);
        }

        foreach (var container in Containers)
        {
            hash.Add(container);
        }

        return hash.ToHashCode();
    }
}
