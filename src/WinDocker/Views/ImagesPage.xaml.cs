using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinDocker.Core.ViewModels;

namespace WinDocker.Views;

public sealed partial class ImagesPage : Page
{
    public ImagesPage()
    {
        ViewModel = App.Services.GetRequiredService<ImagesViewModel>();
        InitializeComponent();
    }

    public ImagesViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshCommand.Execute(null);
    }
}
