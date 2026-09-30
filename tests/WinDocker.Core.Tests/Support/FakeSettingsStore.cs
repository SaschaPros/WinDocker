using WinDocker.Core.Settings;

namespace WinDocker.Core.Tests.Support;

/// <summary>An <see cref="ISettingsStore"/> in memory that remembers what was saved.</summary>
internal sealed class FakeSettingsStore(AppSettings? initial = null) : ISettingsStore
{
    public AppSettings Current { get; private set; } = initial ?? new AppSettings();

    public List<AppSettings> Saved { get; } = [];

    /// <summary>Thrown by <see cref="Save"/> while set.</summary>
    public Exception? SaveFailure { get; set; }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Saved.Add(settings);
        Current = settings;
    }

    /// <summary>A settings service on top of a store that holds <paramref name="refreshIntervalSeconds"/>.</summary>
    public static SettingsService CreateService(int refreshIntervalSeconds = 5) =>
        new(new FakeSettingsStore(new AppSettings(refreshIntervalSeconds)));
}
