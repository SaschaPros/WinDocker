namespace WinDocker.Core.Columns;

/// <summary>Building blocks for the sort keys of the columns.</summary>
internal static class ColumnComparers
{
    /// <summary>Compares text by the current culture, ignoring case. A missing text sorts before any other.</summary>
    public static Comparison<TItem> Text<TItem>(Func<TItem, string?> selector) =>
        (left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(selector(left), selector(right));

    /// <summary>Compares by a value that has a natural order (numbers, dates). A missing value sorts before any other.</summary>
    public static Comparison<TItem> Value<TItem, TValue>(Func<TItem, TValue> selector) =>
        (left, right) => Comparer<TValue>.Default.Compare(selector(left), selector(right));
}
