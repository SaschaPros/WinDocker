using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Collections;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

/// <summary>
/// The existing compose projects of the engine and the commands to start, stop, restart and remove them.
/// The containers of a project are handled one after the other, oldest first when starting and restarting and newest
/// first when stopping and removing, which approximates the order of <c>depends_on</c>. A project's sequence stops at its first failure.
/// Like the compose CLI, start, stop and restart leave the one-off containers of <c>docker compose run</c> alone (starting
/// an exited one would run its command again); removing the project removes them too.
/// </summary>
public sealed partial class ComposeViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private int refreshVersion;
    private bool hasLoaded;
    private IReadOnlyList<ComposeProjectItem> selectedProjects = [];

    public ComposeViewModel(
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
        Projects.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<ComposeProjectItem> Projects { get; } = [];

    /// <summary>The selected rows, as last pushed by <see cref="UpdateSelection"/> and minus the rows a reload removed.</summary>
    public IReadOnlyList<ComposeProjectItem> SelectedProjects => selectedProjects;

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Projects.Count == 0;

    /// <summary>Called by the view with the rows it has selected, whenever the selection changes.</summary>
    public void UpdateSelection(IEnumerable<ComposeProjectItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SetSelection(items.ToArray());
    }

    protected override async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var projects = await docker.ListComposeProjectsAsync();
        if (version != refreshVersion)
        {
            return;
        }

        hasLoaded = true;
        CollectionSync.Sync(
            Projects,
            projects,
            project => project.Name,
            item => item.Name,
            project => new ComposeProjectItem(project) { StatusText = DescribeStatus(project) },
            (item, project) =>
            {
                item.Update(project);
                item.StatusText = DescribeStatus(project);
            });
        OnPropertyChanged(nameof(IsEmpty));
        DropRemovedFromSelection();
    }

    [RelayCommand]
    private Task RefreshAsync() => RunRefreshAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(CanStartSelected))]
    private Task StartAsync()
    {
        var targets = SelectedProjects.Select(item => item.Info).Where(project => project.CanStart).ToList();
        return RunBulkAsync(
            targets,
            project => RunInOrderAsync(Oldest(Services(project)).Where(container => container.CanStart), container => docker.StartContainerAsync(container.Id)),
            project => project.Name,
            RefreshCoreAsync);
    }

    [RelayCommand(CanExecute = nameof(CanStopSelected))]
    private Task StopAsync()
    {
        var targets = SelectedProjects.Select(item => item.Info).Where(project => project.CanStop).ToList();
        return RunBulkAsync(
            targets,
            project => RunInOrderAsync(Newest(Services(project)).Where(container => container.CanStop), container => docker.StopContainerAsync(container.Id)),
            project => project.Name,
            RefreshCoreAsync);
    }

    [RelayCommand(CanExecute = nameof(CanRestartSelected))]
    private Task RestartAsync()
    {
        var targets = SelectedProjects.Select(item => item.Info).Where(project => project.HasServiceContainers).ToList();
        return RunBulkAsync(
            targets,
            project => RunInOrderAsync(Oldest(Services(project)), container => docker.RestartContainerAsync(container.Id)),
            project => project.Name,
            RefreshCoreAsync);
    }

    /// <summary>Removes the containers and the networks of the selected projects, and with the option checked their volumes, like <c>docker compose down</c>.</summary>
    [RelayCommand(CanExecute = nameof(CanDownSelected))]
    private async Task DownAsync()
    {
        var targets = SelectedProjects.Select(item => item.Info).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var confirmation = await ConfirmAsync(dialogs, CreateDownRequest(targets));
        if (!confirmation.Confirmed)
        {
            return;
        }

        var removeVolumes = confirmation.OptionChecked;
        await RunBulkAsync(targets, project => RemoveProjectAsync(project, removeVolumes), project => project.Name, RefreshCoreAsync);
    }

    private static IEnumerable<ComposeContainer> Services(ComposeProjectInfo project) =>
        project.Containers.Where(container => !container.IsOneOff);

    private static IEnumerable<ComposeContainer> Oldest(IEnumerable<ComposeContainer> containers) =>
        containers.OrderBy(container => container.CreatedAt);

    /// <summary>The reverse of <see cref="Oldest"/>, so that containers created within the same second are handled in the opposite order too.</summary>
    private static IEnumerable<ComposeContainer> Newest(IEnumerable<ComposeContainer> containers) => Oldest(containers).Reverse();

    private static async Task RunInOrderAsync(IEnumerable<ComposeContainer> containers, Func<ComposeContainer, Task> action)
    {
        foreach (var container in containers)
        {
            await action(container);
        }
    }

    private async Task RemoveProjectAsync(ComposeProjectInfo project, bool removeVolumes)
    {
        foreach (var container in Newest(project.Containers))
        {
            await docker.RemoveContainerAsync(container.Id, force: true);
        }

        await docker.RemoveComposeNetworksAsync(project.Name);
        if (removeVolumes)
        {
            await docker.RemoveComposeVolumesAsync(project.Name);
        }
    }

    private bool CanStartSelected() => SelectedProjects.Any(item => item.Info.CanStart);

    private bool CanStopSelected() => SelectedProjects.Any(item => item.Info.CanStop);

    private bool CanRestartSelected() => SelectedProjects.Any(item => item.Info.HasServiceContainers);

    private bool CanDownSelected() => HasSelection;

    private string DescribeStatus(ComposeProjectInfo project)
    {
        if (project.RunningCount == 0)
        {
            return Localizer.GetString(ResourceKeys.ComposeStatusExited);
        }

        var key = project.RunningCount == project.TotalCount ? ResourceKeys.ComposeStatusRunning : ResourceKeys.ComposeStatusPartial;
        return Localizer.Format(key, project.RunningCount, project.TotalCount);
    }

    private ConfirmRequest CreateDownRequest(IReadOnlyList<ComposeProjectInfo> targets) => new(
        Localizer.GetString(ResourceKeys.ConfirmComposeDownTitle),
        targets.Count == 1
            ? Localizer.Format(ResourceKeys.ConfirmComposeDownMessage, targets[0].Name)
            : Localizer.Format(ResourceKeys.ConfirmComposeDownMultipleMessage, targets.Count),
        Localizer.GetString(ResourceKeys.DialogRemoveButton),
        Localizer.GetString(ResourceKeys.ConfirmComposeDownVolumesOption));

    private void SetSelection(IReadOnlyList<ComposeProjectItem> items)
    {
        selectedProjects = items;
        OnPropertyChanged(nameof(SelectedProjects));
        SetSelectionCount(items.Count);
        NotifySelectionDependentCommands();
    }

    /// <summary>After a reload: forgets removed rows, and re-evaluates the commands because the containers of a selected project may have changed.</summary>
    private void DropRemovedFromSelection()
    {
        var present = Projects.ToHashSet();
        if (selectedProjects.All(present.Contains))
        {
            NotifySelectionDependentCommands();
            return;
        }

        SetSelection(selectedProjects.Where(present.Contains).ToArray());
    }

    private void NotifySelectionDependentCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        DownCommand.NotifyCanExecuteChanged();
    }
}
