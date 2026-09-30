using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;
using WinDocker.Core.ViewModels;
using WinDocker.Services;

namespace WinDocker;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    /// <summary>The application's services, used by the pages to create their view models.</summary>
    public static IServiceProvider Services { get; } = ConfigureServices();

    /// <summary>The window that hosts the pages; dialogs attach to its content.</summary>
    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        MainWindow = window;
        window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDockerService>(_ => new DockerService());
        services.AddSingleton<ILocalizer, ResourceLocalizer>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ISettingsStore>(_ => new JsonSettingsStore(JsonSettingsStore.DefaultPath));
        services.AddSingleton<SettingsService>();
        services.AddSingleton(TimeProvider.System);

        services.AddTransient<ContainersViewModel>();
        services.AddTransient<ComposeViewModel>();
        services.AddTransient<ImagesViewModel>();
        services.AddTransient<VolumesViewModel>();
        services.AddTransient<LogsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }
}
