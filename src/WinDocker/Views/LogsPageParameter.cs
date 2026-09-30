namespace WinDocker.Views;

/// <summary>The container whose logs <see cref="LogsPage"/> shows.</summary>
public sealed record LogsPageParameter(string ContainerId, string ContainerName);
