using CommunityToolkit.Mvvm.ComponentModel;
using WinDocker.Core.Settings;

namespace WinDocker.Core.Columns;

/// <summary>
/// What the user chose for one list: which columns are shown, in which order, and what the list is sorted by.
/// Every change is saved at once and announced through <see cref="Changed"/>. Meant to be a singleton per list,
/// see <see cref="ListLayouts"/>.
/// </summary>
public sealed class ListLayout : ObservableObject
{
    private readonly string listKey;
    private readonly IReadOnlyList<string> defaultKeys;
    private readonly SettingsService settings;
    private ColumnState[] columns;
    private string? sortKey;
    private bool sortDescending;

    /// <summary>Starts from the stored layout of <paramref name="listKey"/>, if there is one, and adapts it to <paramref name="defaultKeys"/>.</summary>
    /// <param name="listKey">Identifier of the list in the settings file.</param>
    /// <param name="defaultKeys">The keys of the list's columns in their default order.</param>
    /// <param name="settings">Where the layout is read from and saved to.</param>
    public ListLayout(string listKey, IReadOnlyList<string> defaultKeys, SettingsService settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listKey);
        ArgumentNullException.ThrowIfNull(defaultKeys);
        ArgumentNullException.ThrowIfNull(settings);

        this.listKey = listKey;
        this.defaultKeys = defaultKeys.Distinct().ToArray();
        this.settings = settings;
        columns = Merge(settings.GetListLayout(listKey));
        (sortKey, sortDescending) = MergeSort(settings.GetListLayout(listKey));
    }

    /// <summary>Raised once after every change of the columns or the sort order.</summary>
    public event EventHandler? Changed;

    /// <summary>All columns in display order, the hidden ones included.</summary>
    public IReadOnlyList<ColumnState> Columns => columns;

    /// <summary>The key of the column the list is sorted by, or <see langword="null"/> to keep the order of the engine.</summary>
    public string? SortKey => sortKey;

    public bool SortDescending => sortDescending;

    /// <summary>The keys of the shown columns in display order.</summary>
    public IReadOnlyList<string> VisibleKeys => columns.Where(column => column.IsVisible).Select(column => column.Key).ToArray();

    /// <summary>Shows or hides a column. The last shown column cannot be hidden. Unknown keys are ignored.</summary>
    public void SetVisible(string key, bool visible)
    {
        ArgumentNullException.ThrowIfNull(key);

        var index = IndexOf(key);
        if (index < 0 || columns[index].IsVisible == visible)
        {
            return;
        }

        if (!visible && columns.Count(column => column.IsVisible) <= 1)
        {
            return;
        }

        columns[index] = columns[index] with { IsVisible = visible };
        Publish();
    }

    /// <summary>Puts a column at <paramref name="newIndex"/> of <see cref="Columns"/>, clamped to the valid range. Unknown keys are ignored.</summary>
    public void Move(string key, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(key);

        var index = IndexOf(key);
        if (index < 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, columns.Length - 1);
        if (newIndex == index)
        {
            return;
        }

        var column = columns[index];
        var list = columns.ToList();
        list.RemoveAt(index);
        list.Insert(newIndex, column);
        columns = [.. list];
        Publish();
    }

    /// <summary>Cycles the sort order of a column: ascending, descending, none. Another column starts ascending. Unknown keys are ignored.</summary>
    public void ToggleSort(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (IndexOf(key) < 0)
        {
            return;
        }

        if (sortKey != key)
        {
            sortKey = key;
            sortDescending = false;
        }
        else if (!sortDescending)
        {
            sortDescending = true;
        }
        else
        {
            sortKey = null;
            sortDescending = false;
        }

        Publish();
    }

    /// <summary>Shows all columns in their default order and drops the sort order.</summary>
    public void Reset()
    {
        var defaults = defaultKeys.Select(key => new ColumnState(key, true)).ToArray();
        if (columns.SequenceEqual(defaults) && sortKey is null && !sortDescending)
        {
            return;
        }

        columns = defaults;
        sortKey = null;
        sortDescending = false;
        Publish();
    }

    private int IndexOf(string key) => Array.FindIndex(columns, column => column.Key == key);

    /// <summary>Stored columns first (unknown and repeated keys dropped), then the columns the store does not know yet, shown.</summary>
    private ColumnState[] Merge(ListLayoutSettings? stored)
    {
        var known = defaultKeys.ToHashSet();
        var result = new List<ColumnState>();
        var seen = new HashSet<string>();
        foreach (var column in stored?.Columns ?? [])
        {
            if (column?.Key is { } key && known.Contains(key) && seen.Add(key))
            {
                result.Add(new ColumnState(key, column.Visible));
            }
        }

        result.AddRange(defaultKeys.Where(key => !seen.Contains(key)).Select(key => new ColumnState(key, true)));

        // A stored layout that hides everything cannot be shown; fall back to all columns.
        return result.Any(column => column.IsVisible)
            ? [.. result]
            : [.. result.Select(column => column with { IsVisible = true })];
    }

    private (string? Key, bool Descending) MergeSort(ListLayoutSettings? stored) =>
        stored?.SortKey is { } key && defaultKeys.Contains(key) ? (key, stored.SortDescending) : (null, false);

    private void Publish()
    {
        settings.SetListLayout(
            listKey,
            new ListLayoutSettings([.. columns.Select(column => new ColumnSettings(column.Key, column.IsVisible))], sortKey, sortDescending));
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(SortKey));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(VisibleKeys));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
