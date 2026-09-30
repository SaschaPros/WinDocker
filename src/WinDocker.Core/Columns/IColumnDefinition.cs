namespace WinDocker.Core.Columns;

/// <summary>What a view needs to know about a column, independent of the type of the rows.</summary>
public interface IColumnDefinition
{
    /// <summary>Stable identifier of the column; this is what the settings file stores.</summary>
    string Key { get; }

    /// <summary>Name of the resource that holds the header text.</summary>
    string HeaderResourceKey { get; }

    ColumnWidth Width { get; }
}
