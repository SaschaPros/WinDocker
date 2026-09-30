using WinDocker.Core.Columns;

namespace WinDocker.Views.Table;

/// <summary>
/// The tables of the app by key: the layout the user chose and the columns it applies to. Filled once at startup, so
/// <see cref="TableRowPanel"/> and <see cref="TableHeader"/> can be declared in XAML with just the key.
/// </summary>
internal static class TableLayouts
{
    private static readonly Dictionary<string, TableDefinition> Tables = [];

    /// <summary>Makes a table known under <paramref name="key"/>; a second registration replaces the first.</summary>
    public static void Register(string key, ListLayout layout, IEnumerable<IColumnDefinition> columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(columns);

        Tables[key] = new TableDefinition(layout, columns.ToArray());
    }

    /// <summary>The table registered under <paramref name="key"/>, or <see langword="null"/> (a page shown before startup finished, or a typo in XAML).</summary>
    public static TableDefinition? Find(string? key) => key is not null && Tables.TryGetValue(key, out var table) ? table : null;
}

/// <summary>The layout of a table together with the definitions of all its columns.</summary>
internal sealed record TableDefinition(ListLayout Layout, IReadOnlyList<IColumnDefinition> Columns);
