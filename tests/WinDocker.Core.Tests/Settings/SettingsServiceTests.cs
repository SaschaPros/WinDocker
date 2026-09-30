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

    private static ListLayoutSettings Layout(string firstKey = "a") => new([new ColumnSettings(firstKey), new ColumnSettings("b", false)], "b", true);

    [Fact]
    public void GetListLayout_ReturnsTheStoredLayoutOrNull()
    {
        var layout = Layout();
        var service = new SettingsService(new FakeSettingsStore(new AppSettings(5, new Dictionary<string, ListLayoutSettings> { ["containers"] = layout })));

        Assert.Same(layout, service.GetListLayout("containers"));
        Assert.Null(service.GetListLayout("images"));
    }

    [Fact]
    public void SetListLayout_SavesTheLayoutTogetherWithTheInterval()
    {
        var store = new FakeSettingsStore(new AppSettings(30));
        var service = new SettingsService(store);
        var layout = Layout();

        service.SetListLayout("images", layout);

        var saved = Assert.Single(store.Saved);
        Assert.Equal(30, saved.RefreshIntervalSeconds);
        Assert.Same(layout, Assert.Single(saved.ListLayouts!).Value);
        Assert.Same(layout, service.GetListLayout("images"));
    }

    [Fact]
    public void RefreshInterval_KeepsTheStoredLayouts()
    {
        var store = new FakeSettingsStore(new AppSettings(5, new Dictionary<string, ListLayoutSettings> { ["containers"] = Layout() }));
        var service = new SettingsService(store);
        service.SetListLayout("volumes", Layout("z"));

        service.RefreshInterval = TimeSpan.FromSeconds(60);

        var saved = store.Saved[^1];
        Assert.Equal(60, saved.RefreshIntervalSeconds);
        Assert.Equal(["containers", "volumes"], saved.ListLayouts!.Keys.Order());
    }

    [Fact]
    public void SetListLayout_ReplacesTheLayoutOfTheSameList()
    {
        var store = new FakeSettingsStore();
        var service = new SettingsService(store);
        service.SetListLayout("images", Layout("a"));

        service.SetListLayout("images", Layout("b"));

        Assert.Equal("b", store.Current.ListLayouts!["images"].Columns[0].Key);
    }

    [Fact]
    public void SetListLayout_StillAppliesWhenTheSettingsCannotBeSaved()
    {
        var store = new FakeSettingsStore { SaveFailure = new IOException() };
        var service = new SettingsService(store);

        service.SetListLayout("images", Layout());

        Assert.NotNull(service.GetListLayout("images"));
    }

    [Fact]
    public void SetListLayout_RejectsMissingArguments()
    {
        var service = new SettingsService(new FakeSettingsStore());

        Assert.Throws<ArgumentNullException>(() => service.SetListLayout(null!, Layout()));
        Assert.Throws<ArgumentNullException>(() => service.SetListLayout("images", null!));
        Assert.Throws<ArgumentNullException>(() => service.GetListLayout(null!));
    }

    [Fact]
    public void Layouts_SurviveARestartWhenBackedByTheJsonFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "windocker-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            new SettingsService(new JsonSettingsStore(path)).SetListLayout("images", Layout());

            var restarted = new SettingsService(new JsonSettingsStore(path));

            var layout = restarted.GetListLayout("images");
            Assert.NotNull(layout);
            Assert.Equal([new ColumnSettings("a", true), new ColumnSettings("b", false)], layout.Columns);
            Assert.Equal("b", layout.SortKey);
            Assert.True(layout.SortDescending);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
