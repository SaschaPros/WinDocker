namespace WinDocker.Core.Columns;

/// <summary>The width of a column: a fixed number of pixels, or a share of what the fixed columns leave over.</summary>
/// <param name="Value">Pixels, or the weight of the share.</param>
/// <param name="IsStar"><see langword="true"/> for a proportional share (like <c>1.5*</c> in XAML).</param>
public readonly record struct ColumnWidth(double Value, bool IsStar)
{
    public static ColumnWidth Pixels(double pixels) => new(pixels, IsStar: false);

    public static ColumnWidth Star(double weight) => new(weight, IsStar: true);
}
