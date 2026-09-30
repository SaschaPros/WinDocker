namespace WinDocker.Core.Settings;

/// <summary>The settings that survive a restart.</summary>
/// <param name="RefreshIntervalSeconds">Seconds between automatic refreshes of a list; 0 turns them off.</param>
/// <param name="ListLayouts">The column layout of each list by list key (see <c>ListLayouts</c>); <see langword="null"/> when the user never changed one.</param>
public sealed record AppSettings(int RefreshIntervalSeconds = 5, IReadOnlyDictionary<string, ListLayoutSettings>? ListLayouts = null);

/// <summary>The stored layout of one list.</summary>
/// <param name="Columns">All columns in display order.</param>
/// <param name="SortKey">Key of the column the list is sorted by, or <see langword="null"/> for the order of the engine.</param>
/// <param name="SortDescending">Whether the sort order is descending.</param>
public sealed record ListLayoutSettings(IReadOnlyList<ColumnSettings> Columns, string? SortKey = null, bool SortDescending = false);

/// <summary>The stored state of one column.</summary>
public sealed record ColumnSettings(string Key, bool Visible = true);
