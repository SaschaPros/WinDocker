using System.Collections.Specialized;
using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Time.Testing;
using WinDocker.Core.Docker;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class ComposeViewModelTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeDockerService docker = new();
    private readonly FakeDialogService dialogs = new();
    private readonly FakeTimeProvider time = new();
    private readonly SettingsService settings = FakeSettingsStore.CreateService();

    /// <summary>The projects are blog (nothing runs), jobs (one of two containers runs) and shop (all three run), in this order.</summary>
    public ComposeViewModelTests()
    {
        docker.Containers.Add(Member("c1", "shop", "db", "running", 0));
        docker.Containers.Add(Member("c2", "shop", "cache", "running", 1));
        docker.Containers.Add(Member("c3", "shop", "web", "running", 2));
        docker.Containers.Add(Member("c4", "blog", "app", "exited", 0));
        docker.Containers.Add(Member("c5", "jobs", "queue", "running", 0));
        docker.Containers.Add(Member("c6", "jobs", "worker", "exited", 1));
        docker.Containers.Add(new ContainerInfo("x1", "standalone", "image", "cmd", Created, "running", "Up 1 hour", string.Empty));
    }

    private ComposeViewModel CreateViewModel() => new(docker, dialogs, new FakeLocalizer(), settings, time);

    private static ContainerInfo Member(string id, string project, string service, string state, int createdSecond = 0, bool oneOff = false) =>
        new(
            id,
            oneOff ? $"{project}-{service}-run-1" : $"{project}-{service}-1",
            "image",
            "cmd",
            Created.AddSeconds(createdSecond),
            state,
            state,
            string.Empty,
            new ComposeLabels(project, service, $"/srv/{project}", $"/srv/{project}/compose.yaml", oneOff));

    private async Task<ComposeViewModel> LoadedViewModelAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static void Select(ComposeViewModel viewModel, params int[] indexes) =>
        viewModel.UpdateSelection(indexes.Select(index => viewModel.Projects[index]));

    private static string[] Names(ComposeViewModel viewModel) => viewModel.Projects.Select(item => item.Name).ToArray();

    private static ComposeProjectInfo Project(string name) =>
        Assert.Single(ComposeProjects.FromContainers([Member($"{name}-1", name, "web", "running")]));

    /// <summary>Calls of a bulk action: the parallel ones in a stable order, then the reload that follows them.</summary>
    private static string[] Sorted(IEnumerable<string> calls, int parallelCalls) =>
        [.. calls.Take(parallelCalls).Order(StringComparer.Ordinal), .. calls.Skip(parallelCalls)];

    /// <summary>The calls that concern one project, in the order they happened: those that name one of <paramref name="markers"/>.</summary>
    private string[] CallsAbout(params string[] markers) =>
        [.. docker.Calls.Where(call => markers.Any(marker => call.EndsWith($" {marker}", StringComparison.Ordinal) || call.Contains($" {marker} ", StringComparison.Ordinal)))];

    [Fact]
    public async Task Refresh_LoadsTheProjectsByNameAndLeavesOutContainersOfNoProject()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(["blog", "jobs", "shop"], Names(viewModel));
        Assert.Equal(["ListComposeProjects"], docker.Calls);
        Assert.Equal(["app"], viewModel.Projects[0].Info.Services);
        Assert.Equal("/srv/shop", viewModel.Projects[2].Info.WorkingDir);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Refresh_ComputesTheLocalizedStatusOfEachProject()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(ResourceKeys.ComposeStatusExited, viewModel.Projects[0].StatusText);
        Assert.Equal($"{ResourceKeys.ComposeStatusPartial}|1|2", viewModel.Projects[1].StatusText);
        Assert.Equal($"{ResourceKeys.ComposeStatusRunning}|3|3", viewModel.Projects[2].StatusText);
    }

    [Fact]
    public async Task CanExecute_FollowsTheContainersOfTheSelectedProjects()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.False(viewModel.DownCommand.CanExecute(null));

        Select(viewModel, 0);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.DownCommand.CanExecute(null));

        Select(viewModel, 1);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));

        Select(viewModel, 2);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.DownCommand.CanExecute(null));

        Select(viewModel, 0, 2);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));

        viewModel.UpdateSelection([]);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.False(viewModel.DownCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanExecute_LeavesOutContainersThatCannotBeStartedOrStopped()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("f1", "frozen", "web", ContainerStates.Paused));
        docker.Containers.Add(Member("d1", "dead", "web", ContainerStates.Dead));
        var viewModel = await LoadedViewModelAsync();

        Select(viewModel, 0);
        Assert.Equal("dead", viewModel.SelectedProjects[0].Name);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.DownCommand.CanExecute(null));

        Select(viewModel, 1);
        Assert.Equal("frozen", viewModel.SelectedProjects[0].Name);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanExecute_IgnoresOneOffContainersExceptForRemovingTheProject()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("r1", "runs", "web", ContainerStates.Exited, oneOff: true));
        docker.Containers.Add(Member("r2", "runs", "web", ContainerStates.Running, createdSecond: 1, oneOff: true));
        docker.Containers.Add(Member("s1", "shop", "web", ContainerStates.Running));
        docker.Containers.Add(Member("s2", "shop", "job", ContainerStates.Exited, createdSecond: 1, oneOff: true));
        var viewModel = await LoadedViewModelAsync();

        Select(viewModel, 0);
        Assert.Equal("runs", viewModel.SelectedProjects[0].Name);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.RestartCommand.CanExecute(null));
        Assert.True(viewModel.DownCommand.CanExecute(null));

        Select(viewModel, 1);
        Assert.Equal("shop", viewModel.SelectedProjects[0].Name);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectionChange_RaisesCanExecuteChangedOnAllCommands()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = new List<string>();
        viewModel.StartCommand.CanExecuteChanged += (_, _) => raised.Add("start");
        viewModel.StopCommand.CanExecuteChanged += (_, _) => raised.Add("stop");
        viewModel.RestartCommand.CanExecuteChanged += (_, _) => raised.Add("restart");
        viewModel.DownCommand.CanExecuteChanged += (_, _) => raised.Add("down");

        Select(viewModel, 0);

        Assert.Equal(["down", "restart", "start", "stop"], raised.Order());
    }

    [Fact]
    public async Task Selection_ExposesTheCountTheFlagsAndTheItems()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal(0, viewModel.SelectionCount);
        Assert.False(viewModel.HasSelection);
        Assert.Empty(viewModel.SelectedProjects);

        Select(viewModel, 2, 0);

        Assert.Equal(2, viewModel.SelectionCount);
        Assert.True(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);
        Assert.Equal(["shop", "blog"], viewModel.SelectedProjects.Select(item => item.Name));
        Assert.Same(viewModel.Projects[2], viewModel.SelectedProjects[0]);
        Assert.Contains(nameof(ComposeViewModel.SelectionCount), raised);
        Assert.Contains(nameof(ComposeViewModel.SelectedProjects), raised);

        Select(viewModel, 1);

        Assert.True(viewModel.HasSingleSelection);
    }

    [Fact]
    public async Task UpdateSelection_CopiesTheGivenItems()
    {
        var viewModel = await LoadedViewModelAsync();
        var picked = new List<ComposeProjectItem> { viewModel.Projects[0], viewModel.Projects[1] };

        viewModel.UpdateSelection(picked.Where(_ => true));
        picked.Clear();

        Assert.Equal(["blog", "jobs"], viewModel.SelectedProjects.Select(item => item.Name));
    }

    [Fact]
    public async Task Start_StartsTheStoppedContainersOldestFirstAndReloadsOnce()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("o1", "ordered", "web", "exited", 2));
        docker.Containers.Add(Member("o2", "ordered", "db", "exited", 0));
        docker.Containers.Add(Member("o3", "ordered", "cache", "created", 1));
        docker.Containers.Add(Member("o4", "ordered", "mail", "running", 3));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Start o2", "Start o3", "Start o1", "ListComposeProjects"], docker.Calls);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Start_OfSeveralProjectsStartsOnlyWhatCanStartAndReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Start c4", "Start c6", "ListComposeProjects"], Sorted(docker.Calls, 2));
    }

    [Fact]
    public async Task Start_LeavesOutContainersThatCannotBeStarted()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("m1", "mixed", "a", ContainerStates.Exited, 0));
        docker.Containers.Add(Member("m2", "mixed", "b", ContainerStates.Paused, 1));
        docker.Containers.Add(Member("m3", "mixed", "c", ContainerStates.Dead, 2));
        docker.Containers.Add(Member("m4", "mixed", "d", ContainerStates.Restarting, 3));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Start m1", "ListComposeProjects"], docker.Calls);
    }

    [Fact]
    public async Task Stop_StopsTheRunningContainersNewestFirstAndReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(["Stop c3", "Stop c2", "Stop c1", "ListComposeProjects"], docker.Calls);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Stop_OfSeveralProjectsStopsOnlyWhatCanStop()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(["Stop c1", "Stop c2", "Stop c3", "Stop c5", "ListComposeProjects"], Sorted(docker.Calls, 4));
        Assert.Equal(["Stop c3", "Stop c2", "Stop c1"], CallsAbout("c1", "c2", "c3"));
    }

    [Fact]
    public async Task Stop_AlsoStopsPausedAndRestartingContainers()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("m1", "mixed", "a", ContainerStates.Running, 0));
        docker.Containers.Add(Member("m2", "mixed", "b", ContainerStates.Paused, 1));
        docker.Containers.Add(Member("m3", "mixed", "c", ContainerStates.Restarting, 2));
        docker.Containers.Add(Member("m4", "mixed", "d", ContainerStates.Exited, 3));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(["Stop m3", "Stop m2", "Stop m1", "ListComposeProjects"], docker.Calls);
    }

    [Fact]
    public async Task Restart_RestartsAllContainersOldestFirstWhetherTheyRunOrNot()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        docker.Calls.Clear();

        await viewModel.RestartCommand.ExecuteAsync(null);

        Assert.Equal(["Restart c1", "Restart c2", "Restart c3", "ListComposeProjects"], docker.Calls);

        Select(viewModel, 0, 1);
        docker.Calls.Clear();

        await viewModel.RestartCommand.ExecuteAsync(null);

        Assert.Equal(["Restart c4", "Restart c5", "Restart c6", "ListComposeProjects"], Sorted(docker.Calls, 3));
        Assert.Equal(["Restart c5", "Restart c6"], CallsAbout("c5", "c6"));
    }

    [Fact]
    public async Task StartStopAndRestart_LeaveOneOffContainersAlone()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("s1", "shop", "db", ContainerStates.Exited, 0));
        docker.Containers.Add(Member("s2", "shop", "web", ContainerStates.Running, 1));
        docker.Containers.Add(Member("r1", "shop", "migrate", ContainerStates.Exited, 2, oneOff: true));
        docker.Containers.Add(Member("r2", "shop", "shell", ContainerStates.Running, 3, oneOff: true));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);

        docker.Calls.Clear();
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert.Equal(["Start s1", "ListComposeProjects"], docker.Calls);

        docker.Calls.Clear();
        await viewModel.StopCommand.ExecuteAsync(null);
        Assert.Equal(["Stop s2", "ListComposeProjects"], docker.Calls);

        docker.Calls.Clear();
        await viewModel.RestartCommand.ExecuteAsync(null);
        Assert.Equal(["Restart s1", "Restart s2", "ListComposeProjects"], docker.Calls);
    }

    [Fact]
    public async Task Down_RemovesOneOffContainersToo()
    {
        docker.Containers.Clear();
        docker.Containers.Add(Member("s1", "shop", "web", ContainerStates.Running, 0));
        docker.Containers.Add(Member("r1", "shop", "migrate", ContainerStates.Exited, 1, oneOff: true));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Empty(docker.Containers);
        Assert.Contains("RemoveContainer r1 force=True", docker.Calls);
        Assert.Contains("RemoveContainer s1 force=True", docker.Calls);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Down_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        dialogs.Script(new ConfirmResult(false, true));
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
        Assert.Equal(3, viewModel.Projects.Count);
        Assert.Single(viewModel.SelectedProjects);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Down_AsksForConfirmationWithTheProjectNameAndTheVolumesOption()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);

        await viewModel.DownCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmComposeDownTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmComposeDownMessage}|shop", request.Message);
        Assert.Equal(ResourceKeys.DialogRemoveButton, request.PrimaryButtonText);
        Assert.Equal(ResourceKeys.ConfirmComposeDownVolumesOption, request.OptionText);
    }

    [Fact]
    public async Task Down_RemovesTheContainersNewestFirstThenTheNetworksAndReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "RemoveContainer c3 force=True",
                "RemoveContainer c2 force=True",
                "RemoveContainer c1 force=True",
                "RemoveComposeNetworks shop",
                "ListComposeProjects",
            ],
            docker.Calls);
        Assert.Equal(["blog", "jobs"], Names(viewModel));
        Assert.Empty(viewModel.SelectedProjects);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Down_WithTheVolumesOptionRemovesTheVolumesAfterTheNetworks()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        dialogs.Script(new ConfirmResult(true, true));
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "RemoveContainer c3 force=True",
                "RemoveContainer c2 force=True",
                "RemoveContainer c1 force=True",
                "RemoveComposeNetworks shop",
                "RemoveComposeVolumes shop",
                "ListComposeProjects",
            ],
            docker.Calls);
    }

    [Fact]
    public async Task Down_ForcesTheRemovalOfStoppedContainersToo()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal(["RemoveContainer c4 force=True", "RemoveComposeNetworks blog", "ListComposeProjects"], docker.Calls);
    }

    [Fact]
    public async Task Down_OfSeveralProjectsAsksOnceWithTheCountAndReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 2);
        dialogs.Script(new ConfirmResult(true, true));
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmComposeDownTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmComposeDownMultipleMessage}|2", request.Message);
        Assert.Equal(ResourceKeys.DialogRemoveButton, request.PrimaryButtonText);
        Assert.Equal(ResourceKeys.ConfirmComposeDownVolumesOption, request.OptionText);
        Assert.Equal(1, docker.Count("ListComposeProjects"));
        Assert.Equal("ListComposeProjects", docker.Calls[^1]);
        Assert.Equal(
            ["RemoveContainer c4 force=True", "RemoveComposeNetworks blog", "RemoveComposeVolumes blog"],
            CallsAbout("c4", "blog"));
        Assert.Equal(
            [
                "RemoveContainer c3 force=True",
                "RemoveContainer c2 force=True",
                "RemoveContainer c1 force=True",
                "RemoveComposeNetworks shop",
                "RemoveComposeVolumes shop",
            ],
            CallsAbout("c1", "c2", "c3", "shop"));
        Assert.Equal(["jobs"], Names(viewModel));
    }

    [Fact]
    public async Task Down_StopsAProjectAtItsFirstFailure()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        dialogs.Script(new ConfirmResult(true, true));
        docker.MutationFailures["c2"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"container is busy"}""");
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal("container is busy", viewModel.ErrorMessage);
        Assert.Equal(["RemoveContainer c3 force=True", "RemoveContainer c2 force=True", "ListComposeProjects"], docker.Calls);
        Assert.Equal(["c1", "c2"], viewModel.Projects[2].Info.Containers.Select(container => container.Id));
    }

    [Fact]
    public async Task Down_KeepsTheVolumesWhenTheNetworksCannotBeRemoved()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        dialogs.Script(new ConfirmResult(true, true));
        docker.MutationFailures["RemoveComposeNetworks shop"] = new DockerApiException(HttpStatusCode.Forbidden, """{"message":"network has active endpoints"}""");
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal("network has active endpoints", viewModel.ErrorMessage);
        Assert.DoesNotContain("RemoveComposeVolumes shop", docker.Calls);
        Assert.Equal(1, docker.Count("ListComposeProjects"));
    }

    [Fact]
    public async Task Down_ReportsAFailureOfTheVolumeRemoval()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        dialogs.Script(new ConfirmResult(true, true));
        docker.MutationFailures["RemoveComposeVolumes shop"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"volume is in use"}""");

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal("volume is in use", viewModel.ErrorMessage);
        Assert.Equal(["blog", "jobs"], Names(viewModel));
    }

    [Fact]
    public async Task Down_OfSeveralProjectsReportsTheFailedOnesAndStillRemovesTheOthers()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        docker.MutationFailures["RemoveComposeNetworks blog"] = new DockerApiException(HttpStatusCode.Forbidden, """{"message":"network has active endpoints"}""");
        docker.MutationFailures["c3"] = new InvalidOperationException("engine hiccup");
        docker.Calls.Clear();

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.Equal(
            $"{ResourceKeys.BulkPartialFailure}|2|3\nblog: network has active endpoints\nshop: engine hiccup",
            viewModel.ErrorMessage);
        Assert.Equal(1, docker.Count("ListComposeProjects"));
        Assert.Equal(["RemoveContainer c6 force=True", "RemoveContainer c5 force=True", "RemoveComposeNetworks jobs"], CallsAbout("c5", "c6", "jobs"));
        Assert.Equal(["RemoveContainer c3 force=True"], CallsAbout("c1", "c2", "c3", "shop"));

        // A project is made of its containers: blog has none left although its network is still there.
        Assert.Equal(["shop"], Names(viewModel));
    }

    [Fact]
    public async Task Start_StopsAProjectAtItsFirstFailureButStillStartsTheOtherProjects()
    {
        docker.Containers.Add(Member("b1", "batch", "first", "exited", 0));
        docker.Containers.Add(Member("b2", "batch", "second", "exited", 1));
        var viewModel = await LoadedViewModelAsync();
        Assert.Equal("batch", viewModel.Projects[0].Name);
        docker.MutationFailures["b1"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"port is already allocated"}""");
        Select(viewModel, 0, 1);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal("port is already allocated", viewModel.ErrorMessage);
        Assert.Equal(["Start b1"], CallsAbout("b1", "b2"));
        Assert.Equal(["Start c4"], CallsAbout("c4"));
        Assert.Equal(1, docker.Count("ListComposeProjects"));
    }

    [Fact]
    public async Task Stop_ReportsTheFailedProjectsOfSeveralWithTheirNames()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1, 2);
        docker.MutationFailures["c5"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"cannot stop"}""");
        docker.MutationFailures["c3"] = new InvalidOperationException("boom");
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.BulkPartialFailure}|2|2\njobs: cannot stop\nshop: boom", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Restart_DoesNotReloadWhenDockerBecameUnavailable()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        docker.MutationFailure = new DockerUnavailableException("gone");
        docker.Calls.Clear();

        await viewModel.RestartCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.DoesNotContain("ListComposeProjects", docker.Calls);
    }

    [Fact]
    public async Task Down_AsksBeforeTheViewModelBecomesBusy()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        bool? busyWhileAsking = null;
        dialogs.Handler = _ =>
        {
            busyWhileAsking = viewModel.IsBusy;
            return Task.FromResult(new ConfirmResult(true, false));
        };

        await viewModel.DownCommand.ExecuteAsync(null);

        Assert.False(busyWhileAsking);
    }

    [Fact]
    public async Task Refresh_KeepsTheItemInstancesAndUpdatesTheirInfoAndStatus()
    {
        var viewModel = await LoadedViewModelAsync();
        var before = viewModel.Projects.ToArray();
        var changed = new List<string?>();
        before[2].PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        docker.Containers[2] = docker.Containers[2] with { State = "exited", Status = "Exited (0)" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(before, viewModel.Projects);
        Assert.Equal(2, before[2].Info.RunningCount);
        Assert.Equal($"{ResourceKeys.ComposeStatusPartial}|2|3", before[2].StatusText);
        Assert.Equal(["Info", "StatusText"], changed);
    }

    [Fact]
    public async Task Refresh_RaisesNothingForAnUnchangedProject()
    {
        var viewModel = await LoadedViewModelAsync();
        var changed = new List<string?>();
        var events = new List<NotifyCollectionChangedEventArgs>();
        foreach (var item in viewModel.Projects)
        {
            item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        }

        viewModel.Projects.CollectionChanged += (_, e) => events.Add(e);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(changed);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Refresh_InsertsNewProjectsAndRemovesGoneOnesWithoutResetting()
    {
        var viewModel = await LoadedViewModelAsync();
        var before = viewModel.Projects.ToArray();
        var events = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Projects.CollectionChanged += (_, e) => events.Add(e);
        docker.Containers.RemoveAll(container => container.Compose?.Project == "jobs");
        docker.Containers.Add(Member("a1", "alpha", "web", "running"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["alpha", "blog", "shop"], Names(viewModel));
        Assert.Same(before[0], viewModel.Projects[1]);
        Assert.Same(before[2], viewModel.Projects[2]);
        Assert.DoesNotContain(events, e => e.Action == NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public async Task Refresh_KeepsTheSelectionOfTheSameProject()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        var selected = viewModel.Projects[1];
        docker.Containers[5] = docker.Containers[5] with { State = "running", Status = "Up 1 second" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(selected, Assert.Single(viewModel.SelectedProjects));
        Assert.Same(selected, viewModel.Projects[1]);
        Assert.Equal(2, selected.Info.RunningCount);
        Assert.Equal($"{ResourceKeys.ComposeStatusRunning}|2|2", selected.StatusText);
        Assert.Equal(1, viewModel.SelectionCount);
    }

    [Fact]
    public async Task Refresh_DropsProjectsThatAreGoneFromTheSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        docker.Containers.RemoveAll(container => container.Compose?.Project == "jobs");

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["blog", "shop"], viewModel.SelectedProjects.Select(item => item.Name));
        Assert.Equal(2, viewModel.SelectionCount);
        Assert.Contains(nameof(ComposeViewModel.SelectionCount), raised);

        docker.Containers.Clear();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SelectedProjects);
        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.DownCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_ReevaluatesTheCommandsWhenTheContainersOfASelectedProjectChange()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.StartCommand.CanExecute(null));
        var raised = new List<string>();
        viewModel.StartCommand.CanExecuteChanged += (_, _) => raised.Add("start");
        viewModel.StopCommand.CanExecuteChanged += (_, _) => raised.Add("stop");
        foreach (var index in new[] { 0, 1, 2 })
        {
            docker.Containers[index] = docker.Containers[index] with { State = "exited", Status = "Exited (0)" };
        }

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.Contains("start", raised);
        Assert.Contains("stop", raised);
    }

    [Fact]
    public async Task IsEmpty_IsFalseUntilALoadFoundNothing()
    {
        docker.Containers.Clear();
        var viewModel = CreateViewModel();
        Assert.False(viewModel.IsEmpty);

        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEmpty);
        Assert.Contains(nameof(ComposeViewModel.IsEmpty), raised);

        docker.Containers.Add(Member("c1", "shop", "web", "running"));
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task IsEmpty_StaysFalseWhenTheFirstLoadFails()
    {
        docker.ListFailure = new DockerUnavailableException("down");
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsEmpty);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task Refresh_ShowsALocalizedErrorWhenDockerIsUnavailable()
    {
        docker.ListFailure = new DockerUnavailableException("pipe missing");
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);

        docker.ListFailure = null;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(3, viewModel.Projects.Count);
    }

    [Fact]
    public async Task Refresh_IgnoresAnAnswerThatIsSupersededByANewerRefresh()
    {
        var viewModel = CreateViewModel();
        var older = new TaskCompletionSource<IReadOnlyList<ComposeProjectInfo>>();
        var newer = new TaskCompletionSource<IReadOnlyList<ComposeProjectInfo>>();
        var answers = new Queue<TaskCompletionSource<IReadOnlyList<ComposeProjectInfo>>>([older, newer]);
        docker.ListComposeProjectsHandler = () => answers.Dequeue().Task;

        var first = viewModel.RefreshQuietlyAsync();
        var second = viewModel.RefreshQuietlyAsync();
        newer.SetResult([Project("new")]);
        older.SetResult([Project("old")]);
        await first;
        await second;

        Assert.Equal(["new"], Names(viewModel));
    }

    [Fact]
    public async Task Refresh_ReportsBusyWhileTheEngineIsAsked()
    {
        var viewModel = CreateViewModel();
        var gate = new TaskCompletionSource<IReadOnlyList<ComposeProjectInfo>>();
        docker.ListComposeProjectsHandler = () => gate.Task;

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBusy);
        gate.SetResult([]);
        await refresh;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RefreshQuietly_UpdatesTheListInPlaceWithoutBecomingBusy()
    {
        var viewModel = await LoadedViewModelAsync();
        var before = viewModel.Projects.ToArray();
        var busyChanges = 0;
        viewModel.PropertyChanged += (_, e) => busyChanges += e.PropertyName == nameof(ComposeViewModel.IsBusy) ? 1 : 0;
        docker.Containers[3] = docker.Containers[3] with { State = "running", Status = "Up 3 seconds" };
        docker.Containers.Add(Member("a1", "alpha", "web", "running"));

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(["alpha", "blog", "jobs", "shop"], Names(viewModel));
        Assert.Same(before[0], viewModel.Projects[1]);
        Assert.Equal($"{ResourceKeys.ComposeStatusRunning}|1|1", before[0].StatusText);
        Assert.Equal(0, busyChanges);
    }

    [Fact]
    public async Task RefreshQuietly_IsSkippedWhileTheDownConfirmationIsOpen()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        var answer = new TaskCompletionSource<ConfirmResult>();
        dialogs.Handler = _ => answer.Task;
        docker.Calls.Clear();

        var down = viewModel.DownCommand.ExecuteAsync(null);
        await viewModel.RefreshQuietlyAsync();

        Assert.Empty(docker.Calls);

        answer.SetResult(new ConfirmResult(false, false));
        await down;
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(["ListComposeProjects"], docker.Calls);
    }

    [Fact]
    public Task AutoRefresh_KeepsTheProjectsAndTheSelectionCurrent() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        var selected = viewModel.Projects[2];
        docker.Calls.Clear();

        viewModel.StartAutoRefresh();
        docker.Containers[0] = docker.Containers[0] with { State = "exited", Status = "Exited (0) 1 second ago" };
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.Equal(["ListComposeProjects"], docker.Calls);
        Assert.Same(selected, Assert.Single(viewModel.SelectedProjects));
        Assert.Equal($"{ResourceKeys.ComposeStatusPartial}|2|3", selected.StatusText);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.IsBusy);

        docker.Containers.Add(Member("a1", "alpha", "web", "running"));
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.Equal(["alpha", "blog", "jobs", "shop"], Names(viewModel));

        viewModel.StopAutoRefresh();
    });

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        var localizer = new FakeLocalizer();

        Assert.Throws<ArgumentNullException>(() => new ComposeViewModel(null!, dialogs, localizer, settings, time));
        Assert.Throws<ArgumentNullException>(() => new ComposeViewModel(docker, null!, localizer, settings, time));
        Assert.Throws<ArgumentNullException>(() => new ComposeViewModel(docker, dialogs, null!, settings, time));
        Assert.Throws<ArgumentNullException>(() => new ComposeViewModel(docker, dialogs, localizer, null!, time));
        Assert.Throws<ArgumentNullException>(() => new ComposeViewModel(docker, dialogs, localizer, settings, null!));
    }
}
