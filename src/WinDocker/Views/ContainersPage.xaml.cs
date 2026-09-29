using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinDocker.Core.ViewModels;

namespace WinDocker.Views;

public sealed partial class ContainersPage : Page
{
    public ContainersPage()
    {
        ViewModel = App.Services.GetRequiredService<ContainersViewModel>();
        InitializeComponent();
    }

    public ContainersViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshCommand.Execute(null);
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedContainer is { } container)
        {
            Frame.Navigate(typeof(LogsPage), new LogsPageParameter(container.Id, container.Name));
        }
    }
}
