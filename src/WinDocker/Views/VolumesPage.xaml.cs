using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinDocker.Core.ViewModels;

namespace WinDocker.Views;

public sealed partial class VolumesPage : Page
{
    public VolumesPage()
    {
        ViewModel = App.Services.GetRequiredService<VolumesViewModel>();
        InitializeComponent();
    }

    public VolumesViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshCommand.Execute(null);
    }
}
