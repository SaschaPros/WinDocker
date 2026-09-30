using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using WinDocker.Core.Models;
using WinDocker.Core.ViewModels;

namespace WinDocker.Views;

public sealed partial class ContainersPage : Page
{
    public ContainersPage()
    {
        ViewModel = App.Services.GetRequiredService<ContainersViewModel>();
        InitializeComponent();
        ViewModel.ItemsSynced += (_, _) => ListViewSelection.Restore(ItemList, ViewModel.SelectedContainers);
    }

    public ContainersViewModel ViewModel { get; }

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
        ViewModel.UpdateSelection(((ListView)sender).SelectedItems.Cast<ContainerItem>());

    private void List_ContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
        ListViewSelection.PrepareContextMenu<ContainerItem>((ListView)sender, args);

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedContainers.Count == 1)
        {
            var container = ViewModel.SelectedContainers[0];
            Frame.Navigate(typeof(LogsPage), new LogsPageParameter(container.Id, container.Info.Name));
        }
    }
}
