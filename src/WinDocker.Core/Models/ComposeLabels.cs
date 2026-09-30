namespace WinDocker.Core.Models;

/// <summary>What the <c>com.docker.compose.*</c> labels of a container say about the compose project it belongs to.</summary>
/// <param name="Project">Name of the project.</param>
/// <param name="Service">Name of the service; empty when the container has no service label.</param>
/// <param name="WorkingDir">Directory the project was started from, or <see langword="null"/> when it is not labeled.</param>
/// <param name="ConfigFiles">The compose files of the project as the label lists them (comma separated), or <see langword="null"/> when they are not labeled.</param>
/// <param name="IsOneOff">True for a container that <c>docker compose run</c> created next to the services.</param>
public sealed record ComposeLabels(string Project, string Service, string? WorkingDir, string? ConfigFiles, bool IsOneOff);
