namespace WinDocker.Core.ViewModels;

/// <summary>One choice of the refresh interval setting. <paramref name="Seconds"/> = 0 means off.</summary>
public sealed record RefreshIntervalOption(int Seconds, string Label);
