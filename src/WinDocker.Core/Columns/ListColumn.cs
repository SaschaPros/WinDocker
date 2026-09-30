namespace WinDocker.Core.Columns;

/// <summary>A column of a list: how it is identified, labeled, sized and sorted.</summary>
/// <typeparam name="TItem">The type the rows are sorted by (the info record of the row).</typeparam>
public sealed class ListColumn<TItem> : IColumnDefinition
{
    public ListColumn(string key, string headerResourceKey, ColumnWidth width, Comparison<TItem> compare)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerResourceKey);
        ArgumentNullException.ThrowIfNull(compare);

        Key = key;
        HeaderResourceKey = headerResourceKey;
        Width = width;
        Compare = compare;
    }

    public string Key { get; }

    public string HeaderResourceKey { get; }

    public ColumnWidth Width { get; }

    /// <summary>Orders two rows by the value of this column, ascending. Compares the real value, not the displayed text.</summary>
    public Comparison<TItem> Compare { get; }
}
