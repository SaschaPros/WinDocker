using WinDocker.Core.Columns;
using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;

namespace WinDocker.Core.Tests.Columns;

public class ListLayoutTests
{
    private static readonly string[] Defaults = ["a", "b", "c"];

    private readonly FakeSettingsStore store = new();

    private ListLayout Create(string listKey = "list") => new(listKey, Defaults, new SettingsService(store));

    private static FakeSettingsStore StoreWith(ListLayoutSettings layout) =>
        new(new AppSettings(5, new Dictionary<string, ListLayoutSettings> { ["list"] = layout }));

    private static string[] Keys(ListLayout layout) => layout.Columns.Select(column => column.Key).ToArray();

    [Fact]
    public void Constructor_StartsWithAllColumnsShownInDefaultOrderAndNoSort()
    {
        var layout = Create();

        Assert.Equal(Defaults, Keys(layout));
        Assert.All(layout.Columns, column => Assert.True(column.IsVisible));
        Assert.Equal(Defaults, layout.VisibleKeys);
        Assert.Null(layout.SortKey);
        Assert.False(layout.SortDescending);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Constructor_TakesOverTheStoredLayout()
    {
        var stored = StoreWith(new ListLayoutSettings([new("c", true), new("a", false), new("b", true)], "b", true));

        var layout = new ListLayout("list", Defaults, new SettingsService(stored));

        Assert.Equal(["c", "a", "b"], Keys(layout));
        Assert.Equal(["c", "b"], layout.VisibleKeys);
        Assert.Equal("b", layout.SortKey);
        Assert.True(layout.SortDescending);
    }

    [Fact]
    public void Constructor_DropsUnknownAndRepeatedKeysAndAppendsMissingOnesShown()
    {
        var stored = StoreWith(new ListLayoutSettings([new("x", true), new("c", false), new("c", true), new("a", true)]));

        var layout = new ListLayout("list", Defaults, new SettingsService(stored));

        Assert.Equal(["c", "a", "b"], Keys(layout));
        Assert.Equal([false, true, true], layout.Columns.Select(column => column.IsVisible));
    }

    [Fact]
    public void Constructor_IgnoresAStoredSortByAnUnknownColumn()
    {
        var stored = StoreWith(new ListLayoutSettings([new("a", true)], "gone", true));

        var layout = new ListLayout("list", Defaults, new SettingsService(stored));

        Assert.Null(layout.SortKey);
        Assert.False(layout.SortDescending);
    }

    [Fact]
    public void Constructor_ShowsEverythingWhenTheStoredLayoutHidesAllColumns()
    {
        var stored = StoreWith(new ListLayoutSettings([new("a", false), new("b", false), new("c", false)]));

        var layout = new ListLayout("list", Defaults, new SettingsService(stored));

        Assert.Equal(Defaults, layout.VisibleKeys);
    }

    [Fact]
    public void Constructor_ToleratesEmptyStoredColumns()
    {
        var stored = StoreWith(new ListLayoutSettings(null!));

        var layout = new ListLayout("list", Defaults, new SettingsService(stored));

        Assert.Equal(Defaults, Keys(layout));
    }

    [Fact]
    public void Constructor_OnlyReadsTheLayoutOfItsOwnList()
    {
        var stored = StoreWith(new ListLayoutSettings([new("c", true), new("b", true), new("a", true)]));

        var layout = new ListLayout("other", Defaults, new SettingsService(stored));

        Assert.Equal(Defaults, Keys(layout));
    }

    [Fact]
    public void SetVisible_HidesAndShowsAColumnAndSaves()
    {
        var layout = Create();

        layout.SetVisible("b", false);

        Assert.Equal(["a", "c"], layout.VisibleKeys);
        Assert.Equal(["a", "b", "c"], Keys(layout));
        Assert.Equal([new ColumnSettings("a", true), new ColumnSettings("b", false), new ColumnSettings("c", true)], store.Current.ListLayouts!["list"].Columns);

        layout.SetVisible("b", true);

        Assert.Equal(Defaults, layout.VisibleKeys);
        Assert.Equal(2, store.Saved.Count);
    }

    [Fact]
    public void SetVisible_RefusesToHideTheLastShownColumn()
    {
        var layout = Create();
        layout.SetVisible("a", false);
        layout.SetVisible("b", false);
        var changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.SetVisible("c", false);

        Assert.Equal(["c"], layout.VisibleKeys);
        Assert.Equal(0, changes);
        Assert.Equal(2, store.Saved.Count);
    }

    [Fact]
    public void SetVisible_IgnoresUnknownKeysAndNoChange()
    {
        var layout = Create();
        var changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.SetVisible("x", false);
        layout.SetVisible("a", true);

        Assert.Equal(0, changes);
        Assert.Empty(store.Saved);
    }

    [Theory]
    [InlineData("a", 2, new[] { "b", "c", "a" })]
    [InlineData("c", 0, new[] { "c", "a", "b" })]
    [InlineData("b", 0, new[] { "b", "a", "c" })]
    [InlineData("a", 1, new[] { "b", "a", "c" })]
    [InlineData("a", 99, new[] { "b", "c", "a" })]
    [InlineData("c", -5, new[] { "c", "a", "b" })]
    public void Move_PutsTheColumnAtTheIndex(string key, int index, string[] expected)
    {
        var layout = Create();

        layout.Move(key, index);

        Assert.Equal(expected, Keys(layout));
    }

    [Fact]
    public void Move_KeepsVisibilityAndSavesTheOrder()
    {
        var layout = Create();
        layout.SetVisible("a", false);

        layout.Move("a", 2);

        Assert.Equal(["b", "c"], layout.VisibleKeys);
        Assert.Equal([new ColumnSettings("b", true), new ColumnSettings("c", true), new ColumnSettings("a", false)], store.Current.ListLayouts!["list"].Columns);
    }

    [Fact]
    public void Move_DoesNothingForTheCurrentPositionOrAnUnknownKey()
    {
        var layout = Create();
        var changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.Move("a", 0);
        layout.Move("x", 1);

        Assert.Equal(0, changes);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void ToggleSort_CyclesAscendingDescendingNone()
    {
        var layout = Create();

        layout.ToggleSort("b");
        Assert.Equal(("b", false), (layout.SortKey, layout.SortDescending));

        layout.ToggleSort("b");
        Assert.Equal(("b", true), (layout.SortKey, layout.SortDescending));

        layout.ToggleSort("b");
        Assert.Equal((null, false), (layout.SortKey, layout.SortDescending));
    }

    [Fact]
    public void ToggleSort_AnotherColumnStartsAscending()
    {
        var layout = Create();
        layout.ToggleSort("a");
        layout.ToggleSort("a");

        layout.ToggleSort("c");

        Assert.Equal(("c", false), (layout.SortKey, layout.SortDescending));
    }

    [Fact]
    public void ToggleSort_IgnoresUnknownKeys()
    {
        var layout = Create();

        layout.ToggleSort("x");

        Assert.Null(layout.SortKey);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Reset_RestoresDefaultOrderVisibilityAndSort()
    {
        var layout = Create();
        layout.Move("c", 0);
        layout.SetVisible("a", false);
        layout.ToggleSort("b");

        layout.Reset();

        Assert.Equal(Defaults, Keys(layout));
        Assert.Equal(Defaults, layout.VisibleKeys);
        Assert.Null(layout.SortKey);
        Assert.False(layout.SortDescending);
    }

    [Fact]
    public void Reset_OfAnUntouchedLayoutChangesNothing()
    {
        var layout = Create();
        var changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.Reset();

        Assert.Equal(0, changes);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void EveryChangeRaisesChangedAndPropertyChangedOnce()
    {
        var layout = Create();
        var changes = 0;
        var properties = new List<string?>();
        layout.Changed += (_, _) => changes++;
        layout.PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        layout.ToggleSort("a");

        Assert.Equal(1, changes);
        Assert.Contains(nameof(ListLayout.SortKey), properties);
        Assert.Contains(nameof(ListLayout.Columns), properties);
        Assert.Equal(1, properties.Count(name => name == nameof(ListLayout.SortKey)));
    }

    [Fact]
    public void TheLayoutSurvivesARestart()
    {
        var layout = Create();
        layout.Move("c", 0);
        layout.SetVisible("b", false);
        layout.ToggleSort("a");
        layout.ToggleSort("a");

        var restarted = Create();

        Assert.Equal(["c", "a", "b"], Keys(restarted));
        Assert.Equal(["c", "a"], restarted.VisibleKeys);
        Assert.Equal(("a", true), (restarted.SortKey, restarted.SortDescending));
    }

    [Fact]
    public void TheLayoutSurvivesARestartWhenBackedByTheJsonFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "windocker-tests-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var settings = new SettingsService(new JsonSettingsStore(path));
            var layout = new ListLayout("list", Defaults, settings);
            layout.Move("c", 0);
            layout.SetVisible("b", false);
            layout.ToggleSort("c");
            settings.RefreshInterval = TimeSpan.FromSeconds(30);

            var restartedSettings = new SettingsService(new JsonSettingsStore(path));
            var restarted = new ListLayout("list", Defaults, restartedSettings);

            Assert.Equal(TimeSpan.FromSeconds(30), restartedSettings.RefreshInterval);
            Assert.Equal(["c", "a", "b"], Keys(restarted));
            Assert.Equal(["c", "a"], restarted.VisibleKeys);
            Assert.Equal(("c", false), (restarted.SortKey, restarted.SortDescending));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        var settings = new SettingsService(store);

        Assert.Throws<ArgumentException>(() => new ListLayout(" ", Defaults, settings));
        Assert.Throws<ArgumentNullException>(() => new ListLayout("list", null!, settings));
        Assert.Throws<ArgumentNullException>(() => new ListLayout("list", Defaults, null!));
    }
}
