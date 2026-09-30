using CommunityToolkit.Mvvm.ComponentModel;

namespace WinDocker.Core.Settings;

/// <summary>The current settings. Meant to be a singleton: pages and the refresh loops observe it, changes are saved at once.</summary>
public sealed class SettingsService : ObservableObject
{
    private readonly ISettingsStore store;
    private TimeSpan refreshInterval;
    private Dictionary<string, ListLayoutSettings> listLayouts;

    public SettingsService(ISettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
        var stored = store.Load();
        refreshInterval = TimeSpan.FromSeconds(Math.Max(0, stored.RefreshIntervalSeconds));
        listLayouts = stored.ListLayouts is null ? [] : new Dictionary<string, ListLayoutSettings>(stored.ListLayouts);
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

    /// <summary>The stored layout of a list, or <see langword="null"/> when the user never changed it.</summary>
    public ListLayoutSettings? GetListLayout(string listKey)
    {
        ArgumentNullException.ThrowIfNull(listKey);

        return listLayouts.GetValueOrDefault(listKey);
    }

    /// <summary>Remembers the layout of a list and saves all settings.</summary>
    public void SetListLayout(string listKey, ListLayoutSettings layout)
    {
        ArgumentNullException.ThrowIfNull(listKey);
        ArgumentNullException.ThrowIfNull(layout);

        listLayouts[listKey] = layout;
        Save();
    }

    private void Save()
    {
        var seconds = (int)Math.Min(int.MaxValue, Math.Round(refreshInterval.TotalSeconds));
        try
        {
            store.Save(new AppSettings(seconds, listLayouts.Count == 0 ? null : new Dictionary<string, ListLayoutSettings>(listLayouts)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Saving is best effort: the setting still applies until the app closes.
        }
    }
}
