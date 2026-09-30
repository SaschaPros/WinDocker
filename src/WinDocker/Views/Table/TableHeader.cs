using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using WinDocker.Core.Columns;
using WinDocker.Core.Services;
using WinDocker.Services;

namespace WinDocker.Views.Table;

/// <summary>
/// The header row of a table. Every column gets a button that sorts by it; the cells can be dragged to reorder the columns, and a
/// context menu shows and hides columns, moves the column under the pointer and resets the layout. The cells are laid out by a
/// <see cref="TableRowPanel"/> with the same key as the rows, so they line up with them.
/// </summary>
public sealed class TableHeader : UserControl
{
    private const string SortedAscendingGlyph = "";
    private const string SortedDescendingGlyph = "";

    public static readonly DependencyProperty LayoutKeyProperty = DependencyProperty.Register(
        nameof(LayoutKey),
        typeof(string),
        typeof(TableHeader),
        new PropertyMetadata(null, OnLayoutKeyChanged));

    private readonly ILocalizer localizer = App.Services.GetRequiredService<ILocalizer>();
    private readonly Dictionary<string, HeaderCell> cells = [];
    private TableDefinition? table;
    private bool subscribed;
    private string? draggedKey;
    private string? contextKey;

    public TableHeader()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The key the table is registered under, for example "containers".</summary>
    public string? LayoutKey
    {
        get => (string?)GetValue(LayoutKeyProperty);
        set => SetValue(LayoutKeyProperty, value);
    }

    private static void OnLayoutKeyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var header = (TableHeader)sender;
        header.Detach();
        header.table = null;
        header.cells.Clear();
        header.Content = null;
        if (header.IsLoaded)
        {
            header.Attach();
        }
    }

    /// <summary>The element that names a column: the header cell or, for a hit on its content, the cell around it.</summary>
    private static string? ColumnKeyAt(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (TableRowPanel.GetColumnKey(element) is { } key)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Builds the cells on first use and listens to the layout while the header is part of the page.</summary>
    private void Attach()
    {
        if (table is null)
        {
            table = TableLayouts.Find(LayoutKey);
            if (table is null)
            {
                return;
            }

            Build(table);
        }

        if (!subscribed)
        {
            table.Layout.Changed += OnLayoutChanged;
            subscribed = true;
        }

        UpdateSortState();
    }

    private void Detach()
    {
        if (subscribed && table is not null)
        {
            table.Layout.Changed -= OnLayoutChanged;
        }

        subscribed = false;
    }

    private void Build(TableDefinition definition)
    {
        var panel = new TableRowPanel { LayoutKey = LayoutKey };
        foreach (var column in definition.Columns)
        {
            var cell = CreateCell(column);
            TableRowPanel.SetColumnKey(cell.Button, column.Key);
            cells[column.Key] = cell;
            panel.Children.Add(cell.Button);
        }

        var flyout = new MenuFlyout();
        flyout.Opening += OnFlyoutOpening;
        var root = new Grid
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Colors.Transparent),
            ContextFlyout = flyout,
        };
        root.ContextRequested += OnContextRequested;
        root.Children.Add(panel);
        Content = root;
    }

    private HeaderCell CreateCell(IColumnDefinition column)
    {
        var text = new TextBlock
        {
            Text = localizer.GetString(column.HeaderResourceKey),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var glyph = new FontIcon
        {
            FontSize = 10,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(glyph, 1);
        content.Children.Add(text);
        content.Children.Add(glyph);

        var button = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["TableHeaderButtonStyle"],
            CanDrag = true,
            AllowDrop = true,
        };
        AutomationProperties.SetName(button, text.Text);
        button.Click += OnCellClick;
        button.DragStarting += OnCellDragStarting;
        button.DropCompleted += OnCellDropCompleted;
        button.DragOver += OnCellDragOver;
        button.Drop += OnCellDrop;
        return new HeaderCell(button, glyph, text.Text);
    }

    private void OnLayoutChanged(object? sender, EventArgs args) => UpdateSortState();

    /// <summary>Shows the sort direction on the sorted column and tells assistive technology about it.</summary>
    private void UpdateSortState()
    {
        if (table is null)
        {
            return;
        }

        var layout = table.Layout;
        foreach (var (key, cell) in cells)
        {
            var sorted = layout.SortKey == key;
            cell.Glyph.Visibility = sorted ? Visibility.Visible : Visibility.Collapsed;
            cell.Glyph.Glyph = layout.SortDescending ? SortedDescendingGlyph : SortedAscendingGlyph;
            var help = !sorted
                ? localizer.GetString(AppResourceKeys.TableSortByColumn)
                : localizer.GetString(layout.SortDescending ? AppResourceKeys.TableSortedDescending : AppResourceKeys.TableSortedAscending);
            AutomationProperties.SetHelpText(cell.Button, help);
            ToolTipService.SetToolTip(cell.Button, help);
        }
    }

    private void OnCellClick(object sender, RoutedEventArgs args)
    {
        if (table is not null && TableRowPanel.GetColumnKey((DependencyObject)sender) is { } key)
        {
            table.Layout.ToggleSort(key);
        }
    }

    private void OnCellDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        draggedKey = TableRowPanel.GetColumnKey(sender);
        args.Data.SetText(draggedKey ?? string.Empty);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnCellDropCompleted(UIElement sender, DropCompletedEventArgs args) => draggedKey = null;

    /// <summary>Only a column of this table is accepted; text or a column of another table that is dragged over is refused.</summary>
    private void OnCellDragOver(object sender, DragEventArgs args)
    {
        if (draggedKey is null)
        {
            args.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        args.AcceptedOperation = DataPackageOperation.Move;
        args.DragUIOverride.IsGlyphVisible = false;
        args.DragUIOverride.IsCaptionVisible = false;
    }

    /// <summary>The dragged column takes the place of the one it was dropped on.</summary>
    private void OnCellDrop(object sender, DragEventArgs args)
    {
        if (table is not null && draggedKey is not null && TableRowPanel.GetColumnKey((DependencyObject)sender) is { } targetKey)
        {
            table.Layout.Move(draggedKey, IndexOf(targetKey));
        }

        draggedKey = null;
        args.Handled = true;
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
        contextKey = ColumnKeyAt(args.OriginalSource as DependencyObject);

    /// <summary>Builds the menu each time it opens, because the layout and the column under the pointer decide what it offers.</summary>
    private void OnFlyoutOpening(object? sender, object args)
    {
        if (table is null || sender is not MenuFlyout flyout)
        {
            return;
        }

        var layout = table.Layout;
        var visible = layout.VisibleKeys;
        flyout.Items.Clear();
        foreach (var column in layout.Columns)
        {
            var key = column.Key;
            var item = new ToggleMenuFlyoutItem
            {
                Text = HeaderText(key),
                IsChecked = column.IsVisible,
                IsEnabled = !column.IsVisible || visible.Count > 1,
            };
            item.Click += (_, _) => layout.SetVisible(key, item.IsChecked);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var position = contextKey is null ? -1 : IndexOfKey(visible, contextKey);
        var moveLeft = new MenuFlyoutItem { Text = localizer.GetString(AppResourceKeys.TableMoveColumnLeft), IsEnabled = position > 0 };
        moveLeft.Click += (_, _) => MoveNextTo(visible, position, -1);
        var moveRight = new MenuFlyoutItem { Text = localizer.GetString(AppResourceKeys.TableMoveColumnRight), IsEnabled = position >= 0 && position < visible.Count - 1 };
        moveRight.Click += (_, _) => MoveNextTo(visible, position, 1);
        flyout.Items.Add(moveLeft);
        flyout.Items.Add(moveRight);

        flyout.Items.Add(new MenuFlyoutSeparator());
        var reset = new MenuFlyoutItem { Text = localizer.GetString(AppResourceKeys.TableResetColumns) };
        reset.Click += (_, _) => layout.Reset();
        flyout.Items.Add(reset);
    }

    /// <summary>Puts the shown column at <paramref name="position"/> where its shown neighbor is, so the move is visible even when hidden columns lie in between.</summary>
    private void MoveNextTo(IReadOnlyList<string> visible, int position, int direction)
    {
        if (table is not null)
        {
            table.Layout.Move(visible[position], IndexOf(visible[position + direction]));
        }
    }

    private string HeaderText(string key) => cells.TryGetValue(key, out var cell) ? cell.Text : key;

    /// <summary>The position of a column in the layout, hidden columns included.</summary>
    private int IndexOf(string key) => IndexOfKey(table!.Layout.Columns.Select(column => column.Key).ToList(), key);

    private static int IndexOfKey(IReadOnlyList<string> keys, string key)
    {
        for (var index = 0; index < keys.Count; index++)
        {
            if (keys[index] == key)
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record HeaderCell(Button Button, FontIcon Glyph, string Text);
}
