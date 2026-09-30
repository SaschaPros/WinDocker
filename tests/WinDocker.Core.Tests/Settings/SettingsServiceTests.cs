using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;

namespace WinDocker.Core.Tests.Settings;

public class SettingsServiceTests
{
    [Fact]
    public void Constructor_ReadsTheStoredInterval()
    {
        var service = new SettingsService(new FakeSettingsStore(new AppSettings(10)));

        Assert.Equal(TimeSpan.FromSeconds(10), service.RefreshInterval);
    }

    [Fact]
    public void Constructor_UsesTheDefaultOfFiveSecondsForAFreshStore()
    {
        var service = new SettingsService(new FakeSettingsStore());

        Assert.Equal(TimeSpan.FromSeconds(5), service.RefreshInterval);
    }

    [Fact]
    public void Constructor_KeepsAStoredValueThatIsNotOneOfTheChoices()
    {
        var service = new SettingsService(new FakeSettingsStore(new AppSettings(7)));

        Assert.Equal(TimeSpan.FromSeconds(7), service.RefreshInterval);
        Assert.DoesNotContain(7, SettingsService.RefreshIntervalChoices);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-3600)]
    [InlineData(int.MinValue)]
    public void Constructor_TreatsANegativeStoredValueAsOff(int stored)
    {
        var service = new SettingsService(new FakeSettingsStore(new AppSettings(stored)));

        Assert.Equal(TimeSpan.Zero, service.RefreshInterval);
    }

    [Fact]
    public void Constructor_DoesNotWriteAnything()
    {
        var store = new FakeSettingsStore(new AppSettings(-1));

        _ = new SettingsService(store);

        Assert.Empty(store.Saved);
    }

    [Fact]
    public void RefreshInterval_SavesEveryChangeAndRaisesPropertyChanged()
    {
        var store = new FakeSettingsStore();
        var service = new SettingsService(store);
        var raised = new List<string?>();
        service.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        service.RefreshInterval = TimeSpan.FromSeconds(30);
        service.RefreshInterval = TimeSpan.Zero;

        Assert.Equal([new AppSettings(30), new AppSettings(0)], store.Saved);
        Assert.Equal([nameof(SettingsService.RefreshInterval), nameof(SettingsService.RefreshInterval)], raised);
        Assert.Equal(TimeSpan.Zero, service.RefreshInterval);
    }

    [Fact]
    public void RefreshInterval_SettingTheSameValueSavesAndRaisesNothing()
    {
        var store = new FakeSettingsStore(new AppSettings(5));
        var service = new SettingsService(store);
        var raised = new List<string?>();
        service.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        service.RefreshInterval = TimeSpan.FromSeconds(5);

        Assert.Empty(store.Saved);
        Assert.Empty(raised);
    }

    [Fact]
    public void RefreshInterval_ANegativeValueCountsAsOff()
    {
        var store = new FakeSettingsStore();
        var service = new SettingsService(store);

        service.RefreshInterval = TimeSpan.FromSeconds(-4);

        Assert.Equal(TimeSpan.Zero, service.RefreshInterval);
        Assert.Equal([new AppSettings(0)], store.Saved);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void RefreshInterval_StillAppliesWhenTheSettingsCannotBeSaved(Type failure)
    {
        var store = new FakeSettingsStore { SaveFailure = (Exception)Activator.CreateInstance(failure)! };
        var service = new SettingsService(store);

        service.RefreshInterval = TimeSpan.FromSeconds(60);

        Assert.Equal(TimeSpan.FromSeconds(60), service.RefreshInterval);
    }

    [Fact]
    public void RefreshIntervalChoices_AreOffAndTheUsualSteps()
    {
        Assert.Equal([0, 2, 5, 10, 30, 60], SettingsService.RefreshIntervalChoices);
    }

    [Fact]
    public void Constructor_RejectsAMissingStore()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsService(null!));
    }

    [Fact]
    public void ASettingSurvivesARestartWhenBackedByTheJsonFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "windocker-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            new SettingsService(new JsonSettingsStore(path)).RefreshInterval = TimeSpan.FromSeconds(30);

            var restarted = new SettingsService(new JsonSettingsStore(path));

            Assert.Equal(TimeSpan.FromSeconds(30), restarted.RefreshInterval);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
