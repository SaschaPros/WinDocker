using System.Collections.ObjectModel;
using System.Collections.Specialized;
using WinDocker.Core.Collections;
using WinDocker.Core.Models;

namespace WinDocker.Core.Tests.Collections;

public class CollectionSyncTests
{
    private sealed class Row(string key, string value)
    {
        public string Key { get; } = key;

        public string Value { get; set; } = value;

        public override string ToString() => $"{Key}={Value}";
    }

    private static ObservableCollection<Row> Collection(params (string Key, string Value)[] rows) =>
        new(rows.Select(row => new Row(row.Key, row.Value)));

    private static void Sync(ObservableCollection<Row> target, params (string Key, string Value)[] source) =>
        CollectionSync.Sync(
            target,
            source,
            entry => entry.Key,
            row => row.Key,
            entry => new Row(entry.Key, entry.Value),
            (row, entry) => row.Value = entry.Value);

    private static List<NotifyCollectionChangedEventArgs> Record(ObservableCollection<Row> target)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        target.CollectionChanged += (_, e) => events.Add(e);
        return events;
    }

    private static string[] Keys(ObservableCollection<Row> target) => target.Select(row => row.Key).ToArray();

    [Fact]
    public void Sync_IntoAnEmptyCollection_CreatesTheItemsInSourceOrder()
    {
        var target = Collection();

        Sync(target, ("b", "1"), ("a", "2"), ("c", "3"));

        Assert.Equal(["b", "a", "c"], Keys(target));
        Assert.Equal(["1", "2", "3"], target.Select(row => row.Value));
    }

    [Fact]
    public void Sync_RemovesItemsWhoseKeyIsGone()
    {
        var target = Collection(("a", "1"), ("b", "2"), ("c", "3"), ("d", "4"));
        var events = Record(target);

        Sync(target, ("b", "2"), ("d", "4"));

        Assert.Equal(["b", "d"], Keys(target));
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action));
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Sync_WithAnEmptySource_RemovesEverythingWithoutAReset()
    {
        var target = Collection(("a", "1"), ("b", "2"), ("c", "3"));
        var events = Record(target);

        Sync(target);

        Assert.Empty(target);
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Remove, e.Action));
    }

    [Fact]
    public void Sync_UpdatesExistingItemsInPlace()
    {
        var target = Collection(("a", "1"), ("b", "2"));
        var before = target.ToArray();
        var events = Record(target);

        Sync(target, ("a", "one"), ("b", "two"));

        Assert.Equal(["one", "two"], target.Select(row => row.Value));
        Assert.Same(before[0], target[0]);
        Assert.Same(before[1], target[1]);
        Assert.Empty(events);
    }

    [Fact]
    public void Sync_UpdatesEveryExistingItemOnceAndNeverANewOne()
    {
        var target = Collection(("a", "1"), ("b", "2"));
        var updated = new List<string>();

        CollectionSync.Sync(
            target,
            new[] { ("b", "2"), ("c", "3"), ("a", "1") },
            entry => entry.Item1,
            row => row.Key,
            entry => new Row(entry.Item1, entry.Item2),
            (row, _) => updated.Add(row.Key));

        Assert.Equal(["a", "b"], updated.Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Sync_InsertsNewItemsAtTheirPositionInTheSource(int position)
    {
        var target = Collection(("a", "1"), ("b", "2"));
        var events = Record(target);
        string[] expected = [.. new[] { "a", "b" }.Take(position), "new", .. new[] { "a", "b" }.Skip(position)];

        var source = expected.Select(key => (key, "x")).ToArray();
        Sync(target, source);

        Assert.Equal(expected, Keys(target));
        var added = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, added.Action);
        Assert.Equal(position, added.NewStartingIndex);
    }

    [Fact]
    public void Sync_KeepsTheInstancesOfItemsThatSurvive()
    {
        var target = Collection(("a", "1"), ("b", "2"), ("c", "3"), ("d", "4"));
        var byKey = target.ToDictionary(row => row.Key);

        Sync(target, ("x", "9"), ("d", "4"), ("b", "2"), ("y", "8"));

        Assert.Equal(["x", "d", "b", "y"], Keys(target));
        Assert.Same(byKey["d"], target[1]);
        Assert.Same(byKey["b"], target[2]);
    }

    [Fact]
    public void Sync_WhenNothingChangesRelativeOrder_NeverMoves()
    {
        var target = Collection(("a", "1"), ("b", "2"), ("c", "3"), ("d", "4"), ("e", "5"));
        var events = Record(target);

        Sync(target, ("n", "0"), ("a", "1"), ("c", "3"), ("m", "0"), ("e", "5"), ("z", "0"));

        Assert.Equal(["n", "a", "c", "m", "e", "z"], Keys(target));
        Assert.DoesNotContain(events, e => e.Action == NotifyCollectionChangedAction.Move);
    }

    [Fact]
    public void Sync_WhenTheOrderChanged_MovesInsteadOfResetting()
    {
        var target = Collection(("a", "1"), ("b", "2"), ("c", "3"));
        var before = target.ToDictionary(row => row.Key);
        var events = Record(target);

        Sync(target, ("c", "3"), ("a", "1"), ("b", "2"));

        Assert.Equal(["c", "a", "b"], Keys(target));
        Assert.Contains(events, e => e.Action == NotifyCollectionChangedAction.Move);
        Assert.DoesNotContain(events, e => e.Action == NotifyCollectionChangedAction.Reset);
        Assert.All(target, row => Assert.Same(before[row.Key], row));
    }

    [Fact]
    public void Sync_WithIdenticalContent_RaisesNoCollectionEvents()
    {
        var target = Collection(("a", "1"), ("b", "2"));
        var events = Record(target);

        Sync(target, ("a", "1"), ("b", "2"));

        Assert.Empty(events);
    }

    [Fact]
    public void Sync_WhenTheSourceRepeatsAKey_TheFirstEntryWins()
    {
        var target = Collection(("a", "1"));

        Sync(target, ("a", "first"), ("b", "second"), ("a", "third"), ("b", "fourth"));

        Assert.Equal(["a", "b"], Keys(target));
        Assert.Equal(["first", "second"], target.Select(row => row.Value));
    }

    [Fact]
    public void Sync_RemovesItemsThatRepeatAKeyInTheTarget()
    {
        var target = Collection(("a", "1"), ("b", "2"), ("a", "3"));

        Sync(target, ("a", "1"), ("b", "2"));

        Assert.Equal(["a", "b"], Keys(target));
    }

    [Fact]
    public void Sync_WorksWithStructKeys()
    {
        var created = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
        var first = new ImageInfo("sha256:aaa", "nginx", "1", created, 1);
        var second = new ImageInfo("sha256:aaa", "nginx", "2", created, 1);
        var target = new ObservableCollection<ImageItem>();

        CollectionSync.Sync(target, [first, second], ImageKey.From, item => item.Key, info => new ImageItem(info), (item, info) => item.Update(info));
        var kept = target[1];
        CollectionSync.Sync(target, [second with { SizeBytes = 2 }], ImageKey.From, item => item.Key, info => new ImageItem(info), (item, info) => item.Update(info));

        Assert.Same(kept, Assert.Single(target));
        Assert.Equal(2, kept.Info.SizeBytes);
    }

    [Fact]
    public void Sync_RejectsMissingArguments()
    {
        var target = Collection();
        var source = Array.Empty<(string, string)>();
        Func<(string, string), string> sourceKey = entry => entry.Item1;
        Func<Row, string> itemKey = row => row.Key;
        Func<(string, string), Row> create = entry => new Row(entry.Item1, entry.Item2);
        Action<Row, (string, string)> update = (_, _) => { };

        Assert.Throws<ArgumentNullException>(() => CollectionSync.Sync(null!, source, sourceKey, itemKey, create, update));
        Assert.Throws<ArgumentNullException>(() => CollectionSync.Sync(target, null!, sourceKey, itemKey, create, update));
        Assert.Throws<ArgumentNullException>(() => CollectionSync.Sync(target, source, null!, itemKey, create, update));
        Assert.Throws<ArgumentNullException>(() => CollectionSync.Sync(target, source, sourceKey, null!, create, update));
        Assert.Throws<ArgumentNullException>(() => CollectionSync.Sync(target, source, sourceKey, itemKey, null!, update));
        Assert.Throws<ArgumentNullException>(() => CollectionSync.Sync(target, source, sourceKey, itemKey, create, null!));
    }

    [Fact]
    public void Sync_RandomChanges_EndInTheSourceOrderKeepInstancesAndNeverReset()
    {
        var random = new Random(20260930);
        var target = Collection();
        var events = Record(target);
        var pool = Enumerable.Range(0, 12).Select(number => $"k{number}").ToArray();

        for (var round = 0; round < 500; round++)
        {
            var before = target.ToDictionary(row => row.Key);
            var source = pool
                .Where(_ => random.Next(3) > 0)
                .OrderBy(_ => random.Next())
                .Select(key => (key, random.Next(1000).ToString(CultureInfo.InvariantCulture)))
                .ToArray();

            Sync(target, source);

            Assert.Equal(source.Select(entry => entry.key), Keys(target));
            Assert.Equal(source.Select(entry => entry.Item2), target.Select(row => row.Value));
            foreach (var row in target.Where(row => before.ContainsKey(row.Key)))
            {
                Assert.Same(before[row.Key], row);
            }
        }

        Assert.DoesNotContain(events, e => e.Action == NotifyCollectionChangedAction.Reset);
    }
}
