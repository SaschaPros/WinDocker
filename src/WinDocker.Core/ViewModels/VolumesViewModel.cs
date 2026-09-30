using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Collections;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public sealed partial class VolumesViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private int refreshVersion;
    private bool hasLoaded;
    private IReadOnlyList<VolumeItem> selectedVolumes = [];

    public VolumesViewModel(
        IDockerService docker,
        IDialogService dialogs,
        ILocalizer localizer,
        SettingsService settings,
        TimeProvider timeProvider)
        : base(localizer, settings, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);

        this.docker = docker;
        this.dialogs = dialogs;
        Volumes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<VolumeItem> Volumes { get; } = [];

    /// <summary>The selected rows, as last pushed by <see cref="UpdateSelection"/> and minus the rows a reload removed.</summary>
    public IReadOnlyList<VolumeItem> SelectedVolumes => selectedVolumes;

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Volumes.Count == 0;

    /// <summary>Called by the view with the rows it has selected, whenever the selection changes.</summary>
    public void UpdateSelection(IEnumerable<VolumeItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SetSelection(items.ToArray());
    }

    protected override async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var volumes = await docker.ListVolumesAsync();
        if (version != refreshVersion)
        {
            return;
        }

        hasLoaded = true;
        CollectionSync.Sync(
            Volumes,
            volumes,
            volume => volume.Name,
            item => item.Name,
            volume => new VolumeItem(volume),
            (item, volume) => item.Update(volume));
        OnPropertyChanged(nameof(IsEmpty));
        DropRemovedFromSelection();
    }

    [RelayCommand]
    private Task RefreshAsync() => RunRefreshAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveAsync()
    {
        var targets = SelectedVolumes.Select(item => item.Info).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var result = await ConfirmAsync(dialogs, CreateRemoveRequest(targets));
        if (!result.Confirmed)
        {
            return;
        }

        await RunBulkAsync(targets, volume => docker.RemoveVolumeAsync(volume.Name), volume => volume.Name, RefreshCoreAsync);
    }

    private bool CanRemoveSelected() => HasSelection;

    private ConfirmRequest CreateRemoveRequest(IReadOnlyList<VolumeInfo> targets)
    {
        var deleteText = Localizer.GetString(ResourceKeys.DialogDeleteButton);
        return targets.Count == 1
            ? new ConfirmRequest(
                Localizer.GetString(ResourceKeys.ConfirmDeleteVolumeTitle),
                Localizer.Format(ResourceKeys.ConfirmDeleteVolumeMessage, targets[0].Name),
                deleteText)
            : new ConfirmRequest(
                Localizer.GetString(ResourceKeys.ConfirmDeleteVolumesTitle),
                Localizer.Format(ResourceKeys.ConfirmDeleteVolumesMessage, targets.Count),
                deleteText);
    }

    private void SetSelection(IReadOnlyList<VolumeItem> items)
    {
        selectedVolumes = items;
        OnPropertyChanged(nameof(SelectedVolumes));
        SetSelectionCount(items.Count);
        RemoveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>After a reload: forgets removed rows, and re-evaluates the commands.</summary>
    private void DropRemovedFromSelection()
    {
        var present = Volumes.ToHashSet();
        if (selectedVolumes.All(present.Contains))
        {
            RemoveCommand.NotifyCanExecuteChanged();
            return;
        }

        SetSelection(selectedVolumes.Where(present.Contains).ToArray());
    }
}
