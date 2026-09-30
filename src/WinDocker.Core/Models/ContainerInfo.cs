using System.Globalization;
using WinDocker.Core.Docker;

namespace WinDocker.Core.Models;

/// <summary>A container of the engine. <see cref="Compose"/> is <see langword="null"/> unless it belongs to a compose project.</summary>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Command,
    DateTimeOffset CreatedAt,
    string State,
    string Status,
    string Ports,
    ComposeLabels? Compose = null)
{
    public string ShortId => DockerFormat.ShortId(Id);

    public string CreatedText => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>"project / service" for a container of a compose project, empty for any other container.</summary>
    public string ProjectText => Compose switch
    {
        null => string.Empty,
        { Service.Length: 0 } labels => labels.Project,
        var labels => $"{labels.Project} / {labels.Service}",
    };

    public bool CanStart => ContainerStates.CanStart(State);

    public bool CanStop => ContainerStates.CanStop(State);

    public bool RequiresForceRemove => ContainerStates.RequiresForceRemove(State);
}
