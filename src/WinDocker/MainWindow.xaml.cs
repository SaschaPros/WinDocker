using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using WinDocker.Core.Services;
using WinDocker.Services;
using WinDocker.Views;

namespace WinDocker;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Containers"] = typeof(ContainersPage),
        ["Images"] = typeof(ImagesPage),
        ["Volumes"] = typeof(VolumesPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = App.Services.GetRequiredService<ILocalizer>().GetString(AppResourceKeys.AppTitle);
        SizeToWorkArea();
    }

    private void SizeToWorkArea()
    {
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = workArea.Width * 3 / 4;
        var height = workArea.Height * 3 / 4;
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height));
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(ContainersPage));
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string key
            && Pages.TryGetValue(key, out var pageType)
            && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // The logs page belongs to the containers section.
        var sectionPage = e.SourcePageType == typeof(LogsPage) ? typeof(ContainersPage) : e.SourcePageType;
        NavView.SelectedItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is string key && Pages.GetValueOrDefault(key) == sectionPage);
    }
}
