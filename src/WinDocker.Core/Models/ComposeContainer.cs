using WinDocker.Core.Docker;

namespace WinDocker.Core.Models;

/// <summary>A container of a compose project, reduced to what the project view and its commands need.</summary>
/// <param name="Id">Container ID.</param>
/// <param name="Name">Container name.</param>
/// <param name="Service">Name of the service; empty when the container has no service label.</param>
/// <param name="State">State as the engine reports it, for example "running".</param>
/// <param name="CreatedAt">Creation time; the engine gives it in whole seconds.</param>
/// <param name="IsOneOff">True for a container that <c>docker compose run</c> created next to the services.</param>
public sealed record ComposeContainer(string Id, string Name, string Service, string State, DateTimeOffset CreatedAt, bool IsOneOff)
{
    public bool CanStart => ContainerStates.CanStart(State);

    public bool CanStop => ContainerStates.CanStop(State);
}
