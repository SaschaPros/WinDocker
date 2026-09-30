using WinDocker.Core.Columns;
using WinDocker.Core.Models;
using WinDocker.Core.Tests.Support;

namespace WinDocker.Core.Tests.Columns;

public class ListSorterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ListColumn<(string Id, string Text)>> Columns =
    [
        new("text", "Text", ColumnWidth.Star(1), ColumnComparers.Text<(string Id, string Text)>(item => item.Text)),
    ];

    private readonly ListLayout layout = new("list", ["text"], FakeSettingsStore.CreateService());

    private static string[] Ids(IEnumerable<(string Id, string Text)> items) => items.Select(item => item.Id).ToArray();

    private IReadOnlyList<(string Id, string Text)> Sort(params (string Id, string Text)[] items) =>
        ListSorter.Sort(items, layout, Columns, item => item.Id);

    [Fact]
    public void Sort_KeepsTheGivenOrderWithoutASortColumn()
    {
        Assert.Equal(["2", "1", "3"], Ids(Sort(("2", "b"), ("1", "a"), ("3", "c"))));
    }

    [Fact]
    public void Sort_OrdersAscendingAndDescending()
    {
        layout.ToggleSort("text");
        Assert.Equal(["1", "2", "3"], Ids(Sort(("2", "b"), ("1", "a"), ("3", "c"))));

        layout.ToggleSort("text");
        Assert.Equal(["3", "2", "1"], Ids(Sort(("2", "b"), ("1", "a"), ("3", "c"))));
    }

    [Fact]
    public void Sort_BreaksTiesByTheTieBreakerInBothDirections()
    {
        layout.ToggleSort("text");
        Assert.Equal(["1", "2", "3"], Ids(Sort(("3", "a"), ("1", "a"), ("2", "a"))));

        layout.ToggleSort("text");
        Assert.Equal(["1", "2", "3"], Ids(Sort(("3", "a"), ("1", "a"), ("2", "a"))));
    }

    [Fact]
    public void Sort_IgnoresCaseOfText()
    {
        layout.ToggleSort("text");

        Assert.Equal(["1", "2", "3"], Ids(Sort(("3", "c"), ("2", "B"), ("1", "a"))));
    }

    [Fact]
    public void Sort_IgnoresASortKeyWithoutColumn()
    {
        var other = new ListLayout("other", ["missing"], FakeSettingsStore.CreateService());
        other.ToggleSort("missing");

        var sorted = ListSorter.Sort(new (string Id, string Text)[] { ("2", "b"), ("1", "a") }, other, Columns, item => item.Id);

        Assert.Equal(["2", "1"], Ids(sorted));
    }

    private static ContainerInfo Container(string id, string name = "n", string state = "running", string status = "Up", string ports = "", DateTimeOffset? created = null) =>
        new(id, name, "img", "cmd", created ?? Now, state, status, ports);

    private static string[] SortedContainerIds(string key, bool descending, params ContainerInfo[] containers)
    {
        var layout = new ListLayout("containers", ContainerColumns.All.Select(column => column.Key).ToArray(), FakeSettingsStore.CreateService());
        layout.ToggleSort(key);
        if (descending)
        {
            layout.ToggleSort(key);
        }

        return ListSorter.Sort(containers, layout, ContainerColumns.All, container => container.Id).Select(container => container.Id).ToArray();
    }

    [Fact]
    public void ContainerColumns_SortDatesByTheRealValueNotTheText()
    {
        var sorted = SortedContainerIds(
            ContainerColumns.Created,
            descending: false,
            Container("new", created: Now.AddYears(1)),
            Container("old", created: Now.AddYears(-1)),
            Container("mid"));

        Assert.Equal(["old", "mid", "new"], sorted);
    }

    [Fact]
    public void ContainerColumns_SortStatusByStateFirst()
    {
        var sorted = SortedContainerIds(
            ContainerColumns.Status,
            descending: false,
            Container("a", state: "running", status: "Up 3 hours"),
            Container("b", state: "exited", status: "Exited (0) 1 hour ago"),
            Container("c", state: "running", status: "Up 2 hours"));

        Assert.Equal(["b", "c", "a"], sorted);
    }

    [Fact]
    public void ContainerColumns_DeclareTheColumnsOfTheListInOrder()
    {
        Assert.Equal(["name", "project", "image", "status", "ports", "created", "id", "command"], ContainerColumns.All.Select(column => column.Key));
        Assert.Equal(ColumnWidth.Pixels(150), ContainerColumns.All.Single(column => column.Key == "created").Width);
        Assert.Equal(ColumnWidth.Star(1.6), ContainerColumns.All.Single(column => column.Key == "ports").Width);
    }

    [Fact]
    public void ImageColumns_SortSizeNumerically()
    {
        var layout = new ListLayout("images", ImageColumns.All.Select(column => column.Key).ToArray(), FakeSettingsStore.CreateService());
        layout.ToggleSort(ImageColumns.Size);
        var images = new[]
        {
            new ImageInfo("a", "r", "1", Now, 900),
            new ImageInfo("b", "r", "2", Now, 1_000_000),
            new ImageInfo("c", "r", "3", Now, 20_000),
        };

        var sorted = ListSorter.Sort(images, layout, ImageColumns.All, image => image.Reference);

        Assert.Equal(["a", "c", "b"], sorted.Select(image => image.Id));
    }

    [Fact]
    public void VolumeColumns_SortMissingCreationDatesFirstWhenAscending()
    {
        var layout = new ListLayout("volumes", VolumeColumns.All.Select(column => column.Key).ToArray(), FakeSettingsStore.CreateService());
        layout.ToggleSort(VolumeColumns.Created);
        var volumes = new[]
        {
            new VolumeInfo("late", "local", "/a", Now),
            new VolumeInfo("none", "local", "/b", null),
            new VolumeInfo("early", "local", "/c", Now.AddDays(-1)),
        };

        var sorted = ListSorter.Sort(volumes, layout, VolumeColumns.All, volume => volume.Name);

        Assert.Equal(["none", "early", "late"], sorted.Select(volume => volume.Name));
    }

    [Fact]
    public void ComposeColumns_SortStatusByMeaning()
    {
        var layout = new ListLayout("compose", ComposeColumns.All.Select(column => column.Key).ToArray(), FakeSettingsStore.CreateService());
        layout.ToggleSort(ComposeColumns.Status);
        ComposeProjectInfo Project(string name, int running, int total) => new(name, null, null, [], running, total, [], Now);
        var projects = new[] { Project("all", 3, 3), Project("none", 0, 2), Project("some", 1, 3), Project("few", 1, 2) };

        var sorted = ListSorter.Sort(projects, layout, ComposeColumns.All, project => project.Name);

        Assert.Equal(["none", "few", "some", "all"], sorted.Select(project => project.Name));
    }

    [Fact]
    public void ColumnDefinitions_HaveUniqueKeysAndHeaderResources()
    {
        foreach (var columns in new IEnumerable<IColumnDefinition>[] { ContainerColumns.All, ComposeColumns.All, ImageColumns.All, VolumeColumns.All })
        {
            var list = columns.ToList();
            Assert.Equal(list.Count, list.Select(column => column.Key).Distinct().Count());
            Assert.All(list, column => Assert.False(string.IsNullOrWhiteSpace(column.HeaderResourceKey)));
        }
    }

    [Fact]
    public void ListColumn_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentException>(() => new ListColumn<int>(" ", "H", ColumnWidth.Star(1), (a, b) => 0));
        Assert.Throws<ArgumentException>(() => new ListColumn<int>("k", "", ColumnWidth.Star(1), (a, b) => 0));
        Assert.Throws<ArgumentNullException>(() => new ListColumn<int>("k", "H", ColumnWidth.Star(1), null!));
    }
}
