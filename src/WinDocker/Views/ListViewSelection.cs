using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WinDocker.Views;

/// <summary>Selection behavior that the list pages share.</summary>
internal static class ListViewSelection
{
    /// <summary>
    /// Decides what a context menu acts on, like the Explorer does. Over a selected row the selection stays as it is,
    /// over another row that row becomes the only selected one, and over empty space no menu opens.
    /// </summary>
    public static void PrepareContextMenu<TItem>(ListView list, ContextRequestedEventArgs args)
        where TItem : class
    {
        var item = ItemUnder<TItem>(list, args.OriginalSource as DependencyObject);
        if (item is null)
        {
            args.Handled = true;
        }
        else if (!list.SelectedItems.Contains(item))
        {
            list.SelectedItems.Clear();
            list.SelectedItem = item;
        }
    }

    /// <summary>
    /// Selects the rows in <paramref name="wanted"/> that the list view does not show as selected. A list view may treat a move
    /// of an item as remove and insert, which drops the selection of the moved row. Only adds, so it never clears a selection the
    /// user just made. The selection changes it causes are pushed to the view model like any other, which already knows them.
    /// </summary>
    public static void Restore<TItem>(ListView list, IReadOnlyList<TItem> wanted)
        where TItem : class
    {
        foreach (var item in wanted.ToArray())
        {
            if (!list.SelectedItems.Contains(item))
            {
                list.SelectedItems.Add(item);
            }
        }
    }

    /// <summary>The item of the row that contains <paramref name="source"/>: the element that was hit, or the focused row for a keyboard request.</summary>
    private static TItem? ItemUnder<TItem>(ListView list, DependencyObject? source)
        where TItem : class
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem container)
            {
                return list.ItemFromContainer(container) as TItem;
            }
        }

        return null;
    }
}
