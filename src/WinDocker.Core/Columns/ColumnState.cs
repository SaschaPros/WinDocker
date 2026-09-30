namespace WinDocker.Core.Columns;

/// <summary>Whether a column is shown. The position of the state in <see cref="ListLayout.Columns"/> is the position of the column.</summary>
public sealed record ColumnState(string Key, bool IsVisible);
