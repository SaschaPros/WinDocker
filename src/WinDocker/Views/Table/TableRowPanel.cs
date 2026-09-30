using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using WinDocker.Core.Columns;

namespace WinDocker.Views.Table;

/// <summary>
/// Lays its children out as the cells of one table row. A child names its column with <see cref="ColumnKeyProperty"/>;
/// which columns are shown, in which order, comes from the <see cref="ListLayout"/> registered under <see cref="LayoutKey"/>
/// (see <see cref="TableLayouts"/>). Fixed columns keep their width, star columns share what is left. The header and every row use
/// the same panel with the same rules, so their cells always line up. Children of hidden columns are collapsed.
/// </summary>
public sealed class TableRowPanel : Panel
{
    /// <summary>Width that a star column gets when the available width is unbounded, per unit of its weight.</summary>
    private const double UnboundedStarWidth = 100;

    public static readonly DependencyProperty ColumnKeyProperty = DependencyProperty.RegisterAttached(
        "ColumnKey",
        typeof(string),
        typeof(TableRowPanel),
        new PropertyMetadata(null, OnColumnKeyChanged));

    public static readonly DependencyProperty LayoutKeyProperty = DependencyProperty.Register(
        nameof(LayoutKey),
        typeof(string),
        typeof(TableRowPanel),
        new PropertyMetadata(null, OnLayoutKeyChanged));

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(TableRowPanel),
        new PropertyMetadata(12d, OnSpacingChanged));

    private ListLayout? subscribedTo;

    public TableRowPanel()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The key the table is registered under, for example "containers".</summary>
    public string? LayoutKey
    {
        get => (string?)GetValue(LayoutKeyProperty);
        set => SetValue(LayoutKeyProperty, value);
    }

    /// <summary>Space between two shown columns.</summary>
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public static string? GetColumnKey(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (string?)element.GetValue(ColumnKeyProperty);
    }

    public static void SetColumnKey(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(ColumnKeyProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var cells = ResolveCells();
        var widths = ComputeWidths(cells, availableSize.Width);
        var height = 0d;
        for (var index = 0; index < cells.Count; index++)
        {
            cells[index].Child.Measure(new Size(widths[index], availableSize.Height));
            height = Math.Max(height, cells[index].Child.DesiredSize.Height);
        }

        var width = double.IsInfinity(availableSize.Width) ? widths.Sum() + SpacingTotal(cells.Count) : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cells = ResolveCells();
        var widths = ComputeWidths(cells, finalSize.Width);
        var x = 0d;
        for (var index = 0; index < cells.Count; index++)
        {
            cells[index].Child.Arrange(new Rect(x, 0, widths[index], finalSize.Height));
            x += widths[index] + ColumnSpacing;
        }

        return finalSize;
    }

    private static void OnColumnKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((sender as FrameworkElement)?.Parent as TableRowPanel)?.InvalidateMeasure();

    private static void OnLayoutKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var panel = (TableRowPanel)sender;
        panel.Detach();
        if (panel.IsLoaded)
        {
            panel.Attach();
        }

        panel.InvalidateMeasure();
    }

    private static void OnSpacingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((TableRowPanel)sender).InvalidateMeasure();

    /// <summary>Starts listening to the layout while the panel is part of the page; rows come and go with virtualization, so a panel must not stay subscribed after it left.</summary>
    private void Attach()
    {
        if (subscribedTo is not null)
        {
            return;
        }

        subscribedTo = TableLayouts.Find(LayoutKey)?.Layout;
        if (subscribedTo is not null)
        {
            subscribedTo.Changed += OnLayoutChanged;
        }

        InvalidateMeasure();
    }

    private void Detach()
    {
        if (subscribedTo is not null)
        {
            subscribedTo.Changed -= OnLayoutChanged;
            subscribedTo = null;
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs args) => InvalidateMeasure();

    /// <summary>
    /// The children of the shown columns in display order, each with the width definition of its column. Every other child is
    /// collapsed. Without a registered layout no cell is shown.
    /// </summary>
    private List<(UIElement Child, ColumnWidth Width)> ResolveCells()
    {
        var table = TableLayouts.Find(LayoutKey);
        var cells = new List<(UIElement, ColumnWidth)>();
        var shown = new HashSet<UIElement>();
        if (table is not null)
        {
            var byKey = new Dictionary<string, UIElement>();
            foreach (var child in Children)
            {
                if (GetColumnKey(child) is { } key)
                {
                    byKey.TryAdd(key, child);
                }
            }

            foreach (var key in table.Layout.VisibleKeys)
            {
                var definition = table.Columns.FirstOrDefault(column => column.Key == key);
                if (definition is not null && byKey.TryGetValue(key, out var child))
                {
                    cells.Add((child, definition.Width));
                    shown.Add(child);
                }
            }
        }

        foreach (var child in Children)
        {
            var visibility = shown.Contains(child) ? Visibility.Visible : Visibility.Collapsed;
            if (child.Visibility != visibility)
            {
                child.Visibility = visibility;
            }
        }

        return cells;
    }

    /// <summary>Pixel columns get their width, star columns divide the rest by weight. An unbounded width gives star columns a default width.</summary>
    private double[] ComputeWidths(List<(UIElement Child, ColumnWidth Width)> cells, double available)
    {
        var widths = new double[cells.Count];
        var fixedTotal = cells.Where(cell => !cell.Width.IsStar).Sum(cell => cell.Width.Value);
        var weightTotal = cells.Where(cell => cell.Width.IsStar).Sum(cell => cell.Width.Value);
        var remaining = double.IsInfinity(available) ? double.PositiveInfinity : Math.Max(0, available - fixedTotal - SpacingTotal(cells.Count));
        for (var index = 0; index < cells.Count; index++)
        {
            var width = cells[index].Width;
            if (!width.IsStar)
            {
                widths[index] = width.Value;
            }
            else if (double.IsInfinity(remaining))
            {
                widths[index] = width.Value * UnboundedStarWidth;
            }
            else
            {
                widths[index] = weightTotal > 0 ? remaining * width.Value / weightTotal : 0;
            }
        }

        return widths;
    }

    private double SpacingTotal(int cellCount) => Math.Max(0, cellCount - 1) * ColumnSpacing;
}
