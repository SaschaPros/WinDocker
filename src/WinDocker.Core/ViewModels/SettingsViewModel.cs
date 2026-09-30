using CommunityToolkit.Mvvm.ComponentModel;
using WinDocker.Core.Localization;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService settings;

    public SettingsViewModel(SettingsService settings, ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localizer);

        this.settings = settings;

        // A value from the settings file that is not one of the choices still gets an entry, so the selection can show it.
        var current = ToSeconds(settings.RefreshInterval);
        var choices = SettingsService.RefreshIntervalChoices.Contains(current)
            ? SettingsService.RefreshIntervalChoices
            : [.. SettingsService.RefreshIntervalChoices.Append(current).Order()];

        IntervalOptions =
        [
            .. choices.Select(seconds => new RefreshIntervalOption(
                seconds,
                seconds == 0
                    ? localizer.GetString(ResourceKeys.SettingsIntervalOff)
                    : localizer.Format(ResourceKeys.SettingsIntervalSeconds, seconds))),
        ];
        SelectedInterval = IntervalOptions.First(option => option.Seconds == current);
    }

    public IReadOnlyList<RefreshIntervalOption> IntervalOptions { get; }

    /// <summary>The chosen refresh interval; setting it applies and saves the setting.</summary>
    [ObservableProperty]
    public partial RefreshIntervalOption SelectedInterval { get; set; }

    partial void OnSelectedIntervalChanged(RefreshIntervalOption value)
    {
        // A combo box reports null while it rebuilds its items.
        if (value is not null && value.Seconds != ToSeconds(settings.RefreshInterval))
        {
            settings.RefreshInterval = TimeSpan.FromSeconds(value.Seconds);
        }
    }

    private static int ToSeconds(TimeSpan interval) => (int)Math.Min(int.MaxValue, Math.Round(interval.TotalSeconds));
}
