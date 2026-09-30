using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WinDocker.Core.Models;
using WinDocker.Core.ViewModels;

namespace WinDocker.Views;

public sealed partial class ImagesPage : Page
{
    public ImagesPage()
    {
        ViewModel = App.Services.GetRequiredService<ImagesViewModel>();
        InitializeComponent();
        ViewModel.ItemsSynced += (_, _) => ListViewSelection.Restore(ItemList, ViewModel.SelectedImages);
    }

    public ImagesViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshCommand.Execute(null);
        ViewModel.StartAutoRefresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.StopAutoRefresh();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.UpdateSelection(((ListView)sender).SelectedItems.Cast<ImageItem>());

    private void List_ContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
        ListViewSelection.PrepareContextMenu<ImageItem>((ListView)sender, args);
}
