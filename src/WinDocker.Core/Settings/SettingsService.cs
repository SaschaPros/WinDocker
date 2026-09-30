using CommunityToolkit.Mvvm.ComponentModel;

namespace WinDocker.Core.Settings;

/// <summary>The current settings. Meant to be a singleton: pages and the refresh loops observe it, changes are saved at once.</summary>
public sealed class SettingsService : ObservableObject
{
    private readonly ISettingsStore store;
    private TimeSpan refreshInterval;

    public SettingsService(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
        refreshInterval = TimeSpan.FromSeconds(Math.Max(0, store.Load().RefreshIntervalSeconds));
    }

    /// <summary>The intervals offered in the UI, in seconds; 0 is off. Other stored values still work.</summary>
    public static IReadOnlyList<int> RefreshIntervalChoices { get; } = [0, 2, 5, 10, 30, 60];

    /// <summary>The pause between automatic refreshes; <see cref="TimeSpan.Zero"/> turns them off. Negative values count as off.</summary>
    public TimeSpan RefreshInterval
    {
        get => refreshInterval;
        set
        {
            if (SetProperty(ref refreshInterval, value < TimeSpan.Zero ? TimeSpan.Zero : value))
            {
                Save();
            }
        }
    }

    private void Save()
    {
        var seconds = (int)Math.Min(int.MaxValue, Math.Round(refreshInterval.TotalSeconds));
        try
        {
            store.Save(new AppSettings(seconds));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Saving is best effort: the setting still applies until the app closes.
        }
    }
}
