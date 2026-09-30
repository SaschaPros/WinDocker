using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.ViewModels;

public sealed partial class ContainersViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private int refreshVersion;
    private bool hasLoaded;

    public ContainersViewModel(IDockerService docker, IDialogService dialogs, ILocalizer localizer)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);

        this.docker = docker;
        this.dialogs = dialogs;
        Containers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<ContainerInfo> Containers { get; } = [];

    /// <summary>Includes stopped containers, like <c>docker ps -a</c>. Changing it reloads the list.</summary>
    [ObservableProperty]
    public partial bool ShowAll { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial ContainerInfo? SelectedContainer { get; set; }

    public bool HasSelection => SelectedContainer is not null;

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Containers.Count == 0;

    partial void OnShowAllChanged(bool value) => _ = RunSafeAsync(RefreshCoreAsync);

    [RelayCommand]
    private Task RefreshAsync() => RunSafeAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(CanStartSelected))]
    private Task StartAsync() =>
        SelectedContainer is { } container
            ? RunMutationAsync(() => docker.StartContainerAsync(container.Id), RefreshCoreAsync)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanStopSelected))]
    private Task StopAsync() =>
        SelectedContainer is { } container
            ? RunMutationAsync(() => docker.StopContainerAsync(container.Id), RefreshCoreAsync)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveAsync()
    {
        if (SelectedContainer is not { } container)
        {
            return;
        }

        var force = container.RequiresForceRemove;
        var messageKey = force ? ResourceKeys.ConfirmDeleteRunningContainerMessage : ResourceKeys.ConfirmDeleteContainerMessage;
        var confirmed = await dialogs.ConfirmDeleteAsync(
            Localizer.GetString(ResourceKeys.ConfirmDeleteContainerTitle),
            Localizer.Format(messageKey, container.Name));
        if (!confirmed)
        {
            return;
        }

        await RunMutationAsync(() => docker.RemoveContainerAsync(container.Id, force), RefreshCoreAsync);
    }

    private bool CanStartSelected() => SelectedContainer?.CanStart == true;

    private bool CanStopSelected() => SelectedContainer?.CanStop == true;

    private bool CanRemoveSelected() => HasSelection;

    private async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var containers = await docker.ListContainersAsync(ShowAll);
        if (version != refreshVersion)
        {
            // A newer refresh (for example after toggling ShowAll) supersedes this one.
            return;
        }

        var selectedId = SelectedContainer?.Id;
        hasLoaded = true;
        ReplaceAll(Containers, containers);
        OnPropertyChanged(nameof(IsEmpty));
        SelectedContainer = selectedId is null ? null : Containers.FirstOrDefault(container => container.Id == selectedId);
    }
}
