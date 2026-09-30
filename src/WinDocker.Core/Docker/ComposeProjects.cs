using WinDocker.Core.Models;

namespace WinDocker.Core.Docker;

/// <summary>Turns the containers of the engine into the compose projects they belong to.</summary>
public static class ComposeProjects
{
    /// <summary>
    /// Groups the containers by their compose project, ordered by project name. Containers without a project label
    /// are ignored; one-off containers of <c>docker compose run</c> belong to their project and are marked as such.
    /// The result does not depend on the order of <paramref name="containers"/>.
    /// </summary>
    public static IReadOnlyList<ComposeProjectInfo> FromContainers(IEnumerable<ContainerInfo> containers)
    {
        ArgumentNullException.ThrowIfNull(containers);

        return containers
            .Where(container => container.Compose is not null)
            .GroupBy(container => container.Compose!.Project, StringComparer.Ordinal)
            .Select(group => ToProject(group.Key, group))
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static ComposeProjectInfo ToProject(string name, IEnumerable<ContainerInfo> group)
    {
        // Containers created within the same second are told apart by name, so the order is the same on every refresh.
        var members = group
            .OrderBy(container => container.CreatedAt)
            .ThenBy(container => container.Name, StringComparer.Ordinal)
            .ThenBy(container => container.Id, StringComparer.Ordinal)
            .ToList();

        return new ComposeProjectInfo(
            name,
            FirstLabel(members, labels => labels.WorkingDir),
            FirstLabel(members, labels => labels.ConfigFiles),
            [.. members
                .Select(container => container.Compose!.Service)
                .Where(service => service.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.OrdinalIgnoreCase)],
            members.Count(container => container.State == ContainerStates.Running),
            members.Count,
            [.. members.Select(ToComposeContainer)],
            members[0].CreatedAt);
    }

    private static string? FirstLabel(IEnumerable<ContainerInfo> members, Func<ComposeLabels, string?> label) =>
        members.Select(container => label(container.Compose!)).FirstOrDefault(value => !string.IsNullOrEmpty(value));

    private static ComposeContainer ToComposeContainer(ContainerInfo container) => new(
        container.Id,
        container.Name,
        container.Compose!.Service,
        container.State,
        container.CreatedAt,
        container.Compose.IsOneOff);
}
