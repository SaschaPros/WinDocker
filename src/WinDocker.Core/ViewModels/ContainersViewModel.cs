using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Collections;
using WinDocker.Core.Columns;
using WinDocker.Core.Docker;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public sealed partial class ContainersViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private readonly ListLayout layout;
    private int refreshVersion;
    private bool hasLoaded;
    private IReadOnlyList<ContainerInfo> latest = [];
    private IReadOnlyList<ContainerItem> selectedContainers = [];

    public ContainersViewModel(
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
        layout = layouts.Containers;
        // A plain subscription is fine: the page keeps its view model (NavigationCacheMode=Required), so both live as long as the app.
        layout.Changed += (_, _) => ApplyLatest();
        Containers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<ContainerItem> Containers { get; } = [];

    /// <summary>The selected rows, as last pushed by <see cref="UpdateSelection"/> and minus the rows a reload removed.</summary>
    public IReadOnlyList<ContainerItem> SelectedContainers => selectedContainers;

    /// <summary>Includes stopped containers, like <c>docker ps -a</c>. Changing it reloads the list.</summary>
    [ObservableProperty]
    public partial bool ShowAll { get; set; } = true;

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Containers.Count == 0;

    /// <summary>Called by the view with the rows it has selected, whenever the selection changes.</summary>
    public void UpdateSelection(IEnumerable<ContainerItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SetSelection(items.ToArray());
    }

    protected override async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var containers = await docker.ListContainersAsync(ShowAll);
        if (version != refreshVersion)
        {
            // A newer refresh (for example after toggling ShowAll) supersedes this one.
            return;
        }

        hasLoaded = true;
        latest = containers;
        ApplyLatest();
    }

    /// <summary>Shows the last loaded list in the order the layout asks for. Also runs when only the layout changed, without asking the engine again.</summary>
    private void ApplyLatest()
    {
        if (!hasLoaded)
        {
            return;
        }

        var selection = selectedContainers;
        var sorted = ListSorter.Sort(latest, layout, ContainerColumns.All, container => container.Id);
        CollectionSync.Sync(
            Containers,
            sorted,
            container => container.Id,
            item => item.Id,
            container => new ContainerItem(container),
            (item, container) => item.Update(container));
        OnPropertyChanged(nameof(IsEmpty));
        RestoreSelection(selection);
    }

    partial void OnShowAllChanged(bool value) => _ = RunRefreshAsync(RefreshCoreAsync);

    [RelayCommand]
    private Task RefreshAsync() => RunRefreshAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(CanStartSelected))]
    private Task StartAsync()
    {
        var targets = SelectedContainers.Where(item => item.Info.CanStart).Select(item => item.Info).ToList();
        return RunBulkAsync(targets, container => docker.StartContainerAsync(container.Id), container => container.Name, RefreshCoreAsync);
    }

    [RelayCommand(CanExecute = nameof(CanStopSelected))]
    private Task StopAsync()
    {
        var targets = SelectedContainers.Where(item => item.Info.CanStop).Select(item => item.Info).ToList();
        return RunBulkAsync(targets, container => docker.StopContainerAsync(container.Id), container => container.Name, RefreshCoreAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveAsync()
    {
        var targets = SelectedContainers.Select(item => item.Info).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var result = await ConfirmAsync(dialogs, CreateRemoveRequest(targets));
        if (!result.Confirmed)
        {
            return;
        }

        await RunBulkAsync(
            targets,
            container => docker.RemoveContainerAsync(container.Id, container.RequiresForceRemove),
            container => container.Name,
            RefreshCoreAsync);
    }

    /// <summary>Removes all stopped containers after a warning. Needs no selection.</summary>
    [RelayCommand]
    private async Task PruneAsync()
    {
        var request = new ConfirmRequest(
            Localizer.GetString(ResourceKeys.ConfirmPruneContainersTitle),
            Localizer.GetString(ResourceKeys.ConfirmPruneContainersMessage),
            Localizer.GetString(ResourceKeys.DialogPruneButton));
        var confirmation = await ConfirmAsync(dialogs, request);
        if (!confirmation.Confirmed)
        {
            return;
        }

        await RunMutationAsync(
            async () =>
            {
                var result = await docker.PruneContainersAsync();
                StatusMessage = Localizer.Format(ResourceKeys.PruneContainersResult, result.DeletedCount, DockerFormat.Size(result.SpaceReclaimed));
            },
            RefreshCoreAsync);
    }

    private bool CanStartSelected() => SelectedContainers.Any(item => item.Info.CanStart);

    private bool CanStopSelected() => SelectedContainers.Any(item => item.Info.CanStop);

    private bool CanRemoveSelected() => HasSelection;

    private ConfirmRequest CreateRemoveRequest(IReadOnlyList<ContainerInfo> targets)
    {
        var running = targets.Count(container => container.RequiresForceRemove);
        var deleteText = Localizer.GetString(ResourceKeys.DialogDeleteButton);
        if (targets.Count == 1)
        {
            var messageKey = running > 0 ? ResourceKeys.ConfirmDeleteRunningContainerMessage : ResourceKeys.ConfirmDeleteContainerMessage;
            return new ConfirmRequest(
                Localizer.GetString(ResourceKeys.ConfirmDeleteContainerTitle),
                Localizer.Format(messageKey, targets[0].Name),
                deleteText);
        }

        var message = running > 0
            ? Localizer.Format(ResourceKeys.ConfirmDeleteContainersRunningMessage, targets.Count, running)
            : Localizer.Format(ResourceKeys.ConfirmDeleteContainersMessage, targets.Count);
        return new ConfirmRequest(Localizer.GetString(ResourceKeys.ConfirmDeleteContainersTitle), message, deleteText);
    }

    private void SetSelection(IReadOnlyList<ContainerItem> items)
    {
        selectedContainers = items;
        OnPropertyChanged(nameof(SelectedContainers));
        SetSelectionCount(items.Count);
        NotifySelectionDependentCommands();
    }

    /// <summary>
    /// After the list was brought in line: forgets removed rows, puts back rows that a moved row lost from the selection (a list view
    /// may treat a move as remove and insert and report the row as deselected), and re-evaluates the commands because the state of a
    /// selected row may have changed. Then tells the view to select the rows again.
    /// </summary>
    private void RestoreSelection(IReadOnlyList<ContainerItem> before)
    {
        var present = Containers.ToHashSet();
        var kept = before.Where(present.Contains).ToArray();
        if (kept.SequenceEqual(selectedContainers))
        {
            NotifySelectionDependentCommands();
        }
        else
        {
            SetSelection(kept);
        }

        RaiseItemsSynced();
    }

    private void NotifySelectionDependentCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
