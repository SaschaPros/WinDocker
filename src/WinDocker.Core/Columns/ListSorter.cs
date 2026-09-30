namespace WinDocker.Core.Columns;

/// <summary>Sorts the rows of a list the way its <see cref="ListLayout"/> says.</summary>
public static class ListSorter
{
    /// <summary>
    /// Returns <paramref name="items"/> ordered by the sorted column of <paramref name="layout"/>, or in their given order when
    /// no column is sorted. Rows that compare equal are ordered by <paramref name="tieBreaker"/> (ordinal, always ascending), so the order
    /// does not jump around between refreshes.
    /// </summary>
    public static IReadOnlyList<TItem> Sort<TItem>(
        IEnumerable<TItem> items,
        ListLayout layout,
        IReadOnlyList<ListColumn<TItem>> columns,
        Func<TItem, string> tieBreaker)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(tieBreaker);

        var column = layout.SortKey is null ? null : columns.FirstOrDefault(candidate => candidate.Key == layout.SortKey);
        if (column is null)
        {
            return items.ToList();
        }

        var sign = layout.SortDescending ? -1 : 1;
        var comparer = Comparer<TItem>.Create((left, right) =>
        {
            var result = sign * column.Compare(left, right);
            return result != 0 ? result : string.CompareOrdinal(tieBreaker(left), tieBreaker(right));
        });
        return items.Order(comparer).ToList();
    }
}
