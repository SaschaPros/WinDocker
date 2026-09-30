using System.Globalization;
using WinDocker.Core.Docker;

namespace WinDocker.Core.Models;

public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Command,
    DateTimeOffset CreatedAt,
    string State,
    string Status,
    string Ports)
{
    public string ShortId => DockerFormat.ShortId(Id);

    public string CreatedText => CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public bool CanStart => ContainerStates.CanStart(State);

    public bool CanStop => ContainerStates.CanStop(State);

    public bool RequiresForceRemove => ContainerStates.RequiresForceRemove(State);
}
