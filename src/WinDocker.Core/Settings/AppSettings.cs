namespace WinDocker.Core.Settings;

/// <summary>The settings that survive a restart.</summary>
/// <param name="RefreshIntervalSeconds">Seconds between automatic refreshes of a list; 0 turns them off.</param>
public sealed record AppSettings(int RefreshIntervalSeconds = 5);
