using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Collections;
using WinDocker.Core.Columns;
using WinDocker.Core.Docker;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public sealed partial class VolumesViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private readonly ListLayout layout;
    private int refreshVersion;
    private bool hasLoaded;
    private IReadOnlyList<VolumeInfo> latest = [];
    private IReadOnlyList<VolumeItem> selectedVolumes = [];

    public VolumesViewModel(
        IDockerService docker,
        IDialogService dialogs,
        ILocalizer localizer,
        SettingsService settings,
        ListLayouts layouts,
        TimeProvider timeProvider)
        : base(localizer, settings, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(layouts);

        this.docker = docker;
        this.dialogs = dialogs;
        layout = layouts.Volumes;
        // A plain subscription is fine: the page keeps its view model (NavigationCacheMode=Required), so both live as long as the app.
        layout.Changed += (_, _) => ApplyLatest();
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
        latest = volumes;
        ApplyLatest();
    }

    /// <summary>Shows the last loaded list in the order the layout asks for. Also runs when only the layout changed, without asking the engine again.</summary>
    private void ApplyLatest()
    {
        if (!hasLoaded)
        {
            return;
        }

        var selection = selectedVolumes;
        var sorted = ListSorter.Sort(latest, layout, VolumeColumns.All, volume => volume.Name);
        CollectionSync.Sync(
            Volumes,
            sorted,
            volume => volume.Name,
            item => item.Name,
            volume => new VolumeItem(volume),
            (item, volume) => item.Update(volume));
        OnPropertyChanged(nameof(IsEmpty));
        RestoreSelection(selection);
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

    /// <summary>Removes the anonymous volumes that no container uses after a warning, or with the option checked the named ones as well. Needs no selection.</summary>
    [RelayCommand]
    private async Task PruneAsync()
    {
        var request = new ConfirmRequest(
            Localizer.GetString(ResourceKeys.ConfirmPruneVolumesTitle),
            Localizer.GetString(ResourceKeys.ConfirmPruneVolumesMessage),
            Localizer.GetString(ResourceKeys.DialogPruneButton),
            Localizer.GetString(ResourceKeys.ConfirmPruneVolumesNamedOption));
        var confirmation = await ConfirmAsync(dialogs, request);
        if (!confirmation.Confirmed)
        {
            return;
        }

        await RunMutationAsync(
            async () =>
            {
                var result = await docker.PruneVolumesAsync(confirmation.OptionChecked);
                StatusMessage = Localizer.Format(ResourceKeys.PruneVolumesResult, result.DeletedCount, DockerFormat.Size(result.SpaceReclaimed));
            },
            RefreshCoreAsync);
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

    /// <summary>
    /// After the list was brought in line: forgets removed rows, puts back rows that a moved row lost from the selection (a list view
    /// may treat a move as remove and insert and report the row as deselected), and re-evaluates the commands. Then tells the view
    /// to select the rows again.
    /// </summary>
    private void RestoreSelection(IReadOnlyList<VolumeItem> before)
    {
        var present = Volumes.ToHashSet();
        var kept = before.Where(present.Contains).ToArray();
        if (kept.SequenceEqual(selectedVolumes))
        {
            RemoveCommand.NotifyCanExecuteChanged();
        }
        else
        {
            SetSelection(kept);
        }

        RaiseItemsSynced();
    }
}
