using WinDocker.Core.Localization;
using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly FakeSettingsStore store;
    private readonly SettingsService settings;

    public SettingsViewModelTests()
    {
        store = new FakeSettingsStore(new AppSettings(5));
        settings = new SettingsService(store);
    }

    private SettingsViewModel CreateViewModel() => new(settings, new FakeLocalizer());

    private static SettingsViewModel CreateViewModelFor(int storedSeconds, out FakeSettingsStore store)
    {
        store = new FakeSettingsStore(new AppSettings(storedSeconds));
        return new SettingsViewModel(new SettingsService(store), new FakeLocalizer());
    }

    [Fact]
    public void IntervalOptions_OfferTheChoicesWithLocalizedLabels()
    {
        var viewModel = CreateViewModel();

        Assert.Equal([0, 2, 5, 10, 30, 60], viewModel.IntervalOptions.Select(option => option.Seconds));
        Assert.Equal(
            [
                ResourceKeys.SettingsIntervalOff,
                $"{ResourceKeys.SettingsIntervalSeconds}|2",
                $"{ResourceKeys.SettingsIntervalSeconds}|5",
                $"{ResourceKeys.SettingsIntervalSeconds}|10",
                $"{ResourceKeys.SettingsIntervalSeconds}|30",
                $"{ResourceKeys.SettingsIntervalSeconds}|60",
            ],
            viewModel.IntervalOptions.Select(option => option.Label));
    }

    [Fact]
    public void SelectedInterval_StartsAtTheCurrentSetting()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(5, viewModel.SelectedInterval.Seconds);
        Assert.Contains(viewModel.SelectedInterval, viewModel.IntervalOptions);
        Assert.Same(viewModel.IntervalOptions.Single(option => option.Seconds == 5), viewModel.SelectedInterval);
    }

    [Fact]
    public void SelectedInterval_StartsAtOffWhenRefreshingIsOff()
    {
        var viewModel = CreateViewModelFor(0, out _);

        Assert.Equal(0, viewModel.SelectedInterval.Seconds);
        Assert.Equal(ResourceKeys.SettingsIntervalOff, viewModel.SelectedInterval.Label);
    }

    [Fact]
    public void IntervalOptions_GetAnEntryForAStoredValueThatIsNotAChoice()
    {
        var viewModel = CreateViewModelFor(7, out _);

        Assert.Equal([0, 2, 5, 7, 10, 30, 60], viewModel.IntervalOptions.Select(option => option.Seconds));
        Assert.Equal(7, viewModel.SelectedInterval.Seconds);
        Assert.Equal($"{ResourceKeys.SettingsIntervalSeconds}|7", viewModel.SelectedInterval.Label);
        Assert.Contains(viewModel.SelectedInterval, viewModel.IntervalOptions);
    }

    [Fact]
    public void IntervalOptions_AddedEntriesGoAtTheEndForALargeStoredValue()
    {
        var viewModel = CreateViewModelFor(3600, out _);

        Assert.Equal([0, 2, 5, 10, 30, 60, 3600], viewModel.IntervalOptions.Select(option => option.Seconds));
    }

    [Fact]
    public void Opening_DoesNotSaveAnything()
    {
        _ = CreateViewModelFor(7, out var storeOfSeven);
        _ = CreateViewModel();

        Assert.Empty(storeOfSeven.Saved);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void SelectedInterval_AppliesAndSavesTheChoice()
    {
        var viewModel = CreateViewModel();
        var thirty = viewModel.IntervalOptions.Single(option => option.Seconds == 30);

        viewModel.SelectedInterval = thirty;

        Assert.Same(thirty, viewModel.SelectedInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RefreshInterval);
        Assert.Equal([new AppSettings(30)], store.Saved);
    }

    [Fact]
    public void SelectedInterval_OffTurnsRefreshingOff()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedInterval = viewModel.IntervalOptions[0];

        Assert.Equal(TimeSpan.Zero, settings.RefreshInterval);
        Assert.Equal([new AppSettings(0)], store.Saved);
    }

    [Fact]
    public void SelectedInterval_RaisesPropertyChanged()
    {
        var viewModel = CreateViewModel();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.SelectedInterval = viewModel.IntervalOptions[1];

        Assert.Equal([nameof(SettingsViewModel.SelectedInterval)], raised);
    }

    [Fact]
    public void SelectedInterval_SelectingTheCurrentChoiceAgainSavesNothing()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedInterval = viewModel.IntervalOptions.Single(option => option.Seconds == 5);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public void SelectedInterval_IgnoresNullFromAComboBoxThatRebuildsItsItems()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedInterval = null!;

        Assert.Equal(TimeSpan.FromSeconds(5), settings.RefreshInterval);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(null!, new FakeLocalizer()));
        Assert.Throws<ArgumentNullException>(() => new SettingsViewModel(settings, null!));
    }
}
