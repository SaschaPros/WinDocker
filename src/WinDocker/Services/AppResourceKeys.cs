namespace WinDocker.Services;

/// <summary>Keys of the app's own texts that code (not XAML) looks up in the resource file.</summary>
internal static class AppResourceKeys
{
    public const string AppTitle = "AppTitle";

    public const string DialogCancelButton = "Dialog_CancelButton";

    /// <summary>Help text of the header of the column the list is sorted by, ascending.</summary>
    public const string TableSortedAscending = "Table_SortedAscending";

    /// <summary>Help text of the header of the column the list is sorted by, descending.</summary>
    public const string TableSortedDescending = "Table_SortedDescending";

    /// <summary>Help text of the header of a column the list is not sorted by.</summary>
    public const string TableSortByColumn = "Table_SortByColumn";

    /// <summary>Header menu: moves the column under the pointer one place to the left.</summary>
    public const string TableMoveColumnLeft = "Table_MoveColumnLeft";

    /// <summary>Header menu: moves the column under the pointer one place to the right.</summary>
    public const string TableMoveColumnRight = "Table_MoveColumnRight";

    /// <summary>Header menu: shows all columns in their default order and drops the sort order.</summary>
    public const string TableResetColumns = "Table_ResetColumns";
}
