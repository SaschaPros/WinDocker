using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.ViewModels;

public sealed partial class VolumesViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private int refreshVersion;
    private bool hasLoaded;

    public VolumesViewModel(IDockerService docker, IDialogService dialogs, ILocalizer localizer)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);

        this.docker = docker;
        this.dialogs = dialogs;
        Volumes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<VolumeInfo> Volumes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial VolumeInfo? SelectedVolume { get; set; }

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Volumes.Count == 0;

    [RelayCommand]
    private Task RefreshAsync() => RunSafeAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveAsync()
    {
        if (SelectedVolume is not { } volume)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmDeleteAsync(
            Localizer.GetString(ResourceKeys.ConfirmDeleteVolumeTitle),
            Localizer.Format(ResourceKeys.ConfirmDeleteVolumeMessage, volume.Name));
        if (!confirmed)
        {
            return;
        }

        await RunMutationAsync(() => docker.RemoveVolumeAsync(volume.Name), RefreshCoreAsync);
    }

    private bool HasSelection() => SelectedVolume is not null;

    private async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var volumes = await docker.ListVolumesAsync();
        if (version != refreshVersion)
        {
            return;
        }

        var selectedName = SelectedVolume?.Name;
        hasLoaded = true;
        ReplaceAll(Volumes, volumes);
        OnPropertyChanged(nameof(IsEmpty));
        SelectedVolume = selectedName is null ? null : Volumes.FirstOrDefault(volume => volume.Name == selectedName);
    }
}
