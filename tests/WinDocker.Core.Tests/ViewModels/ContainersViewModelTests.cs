using System.Collections.Specialized;
using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Time.Testing;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class ContainersViewModelTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeDockerService docker = new();
    private readonly FakeDialogService dialogs = new();
    private readonly FakeTimeProvider time = new();
    private readonly SettingsService settings = FakeSettingsStore.CreateService();

    public ContainersViewModelTests()
    {
        docker.Containers.Add(Container("c1", "web", "running"));
        docker.Containers.Add(Container("c2", "db", "exited"));
        docker.Containers.Add(Container("c3", "job", "created"));
    }

    private ContainersViewModel CreateViewModel() => new(docker, dialogs, new FakeLocalizer(), settings, time);

    private static ContainerInfo Container(string id, string name, string state) =>
        new(id, name, "image", "cmd", Created, state, state, string.Empty);

    private async Task<ContainersViewModel> LoadedViewModelAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static void Select(ContainersViewModel viewModel, params int[] indexes) =>
        viewModel.UpdateSelection(indexes.Select(index => viewModel.Containers[index]));

    private static string[] Ids(ContainersViewModel viewModel) => viewModel.Containers.Select(item => item.Id).ToArray();

    /// <summary>Calls of a bulk action: the parallel ones in a stable order, then the reload that follows them.</summary>
    private static string[] Sorted(IEnumerable<string> calls, int parallelCalls) =>
        [.. calls.Take(parallelCalls).Order(StringComparer.Ordinal), .. calls.Skip(parallelCalls)];

    [Fact]
    public async Task Refresh_LoadsAllContainersByDefault()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.True(viewModel.ShowAll);
        Assert.Equal(["c1", "c2", "c3"], Ids(viewModel));
        Assert.Equal(["ListContainers all=True"], docker.Calls);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ShowAll_ReloadsWithTheNewValue()
    {
        var viewModel = await LoadedViewModelAsync();

        viewModel.ShowAll = false;

        Assert.Equal("ListContainers all=False", docker.Calls[^1]);
        Assert.Equal(["c1"], Ids(viewModel));

        viewModel.ShowAll = true;

        Assert.Equal("ListContainers all=True", docker.Calls[^1]);
        Assert.Equal(3, viewModel.Containers.Count);
    }

    [Fact]
    public async Task CanExecute_FollowsTheStateOfTheSelectedContainer()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 0);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 1);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 2);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));

        viewModel.UpdateSelection([]);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task CanExecute_WithAMixedSelectionIsEnabledWhenAnySelectedContainerQualifies()
    {
        var viewModel = await LoadedViewModelAsync();

        Select(viewModel, 0, 1);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 1, 2);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 0);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectionChange_RaisesCanExecuteChangedOnAllActionCommands()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = new List<string>();
        viewModel.StartCommand.CanExecuteChanged += (_, _) => raised.Add("start");
        viewModel.StopCommand.CanExecuteChanged += (_, _) => raised.Add("stop");
        viewModel.RemoveCommand.CanExecuteChanged += (_, _) => raised.Add("remove");

        Select(viewModel, 0);

        Assert.Equal(["remove", "start", "stop"], raised.Order());
    }

    [Fact]
    public async Task Selection_ExposesTheCountAndTheFlags()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal(0, viewModel.SelectionCount);
        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);
        Assert.Empty(viewModel.SelectedContainers);

        Select(viewModel, 0);

        Assert.Equal(1, viewModel.SelectionCount);
        Assert.True(viewModel.HasSelection);
        Assert.True(viewModel.HasSingleSelection);
        Assert.Contains(nameof(ContainersViewModel.SelectionCount), raised);
        Assert.Contains(nameof(ContainersViewModel.HasSelection), raised);
        Assert.Contains(nameof(ContainersViewModel.HasSingleSelection), raised);
        Assert.Contains(nameof(ContainersViewModel.SelectedContainers), raised);

        Select(viewModel, 0, 2);

        Assert.Equal(2, viewModel.SelectionCount);
        Assert.True(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);

        viewModel.UpdateSelection([]);

        Assert.Equal(0, viewModel.SelectionCount);
        Assert.False(viewModel.HasSelection);
    }

    [Fact]
    public async Task UpdateSelection_ReplacesTheSelectionWithTheGivenItems()
    {
        var viewModel = await LoadedViewModelAsync();

        Select(viewModel, 2, 0);

        Assert.Equal(["c3", "c1"], viewModel.SelectedContainers.Select(item => item.Id));
        Assert.Same(viewModel.Containers[2], viewModel.SelectedContainers[0]);

        Select(viewModel, 1);

        Assert.Equal(["c2"], viewModel.SelectedContainers.Select(item => item.Id));
    }

    [Fact]
    public async Task Start_StartsOnlyTheSelectedContainersThatCanStartAndReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Start c2", "Start c3", "ListContainers all=True"], Sorted(docker.Calls, 2));
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Stop_StopsOnlyTheSelectedContainersThatCanStopAndReloadsOnce()
    {
        docker.Containers.Add(Container("c4", "cache", "paused"));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2, 3);
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(["Stop c1", "Stop c4", "ListContainers all=True"], Sorted(docker.Calls, 2));
    }

    [Fact]
    public async Task Start_WorksOnASingleSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Start c2", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Stop_WorksOnASingleSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(["Stop c1", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Start_ReportsAPartialFailureAfterOneReload()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailures["c2"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"port is already allocated"}""");
        docker.MutationFailures["c3"] = new InvalidOperationException("no such image");
        Select(viewModel, 1, 2);
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(
            $"{ResourceKeys.BulkPartialFailure}|2|2\ndb: port is already allocated\njob: no such image",
            viewModel.ErrorMessage);
        Assert.Equal(["Start c2", "Start c3", "ListContainers all=True"], Sorted(docker.Calls, 2));
    }

    [Fact]
    public async Task Remove_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        dialogs.Decline();
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
        Assert.Equal(3, viewModel.Containers.Count);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Remove_OfSeveralDoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        dialogs.Decline();
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
        Assert.Equal(3, viewModel.SelectedContainers.Count);
    }

    [Fact]
    public async Task Remove_ConfirmsWithTheContainerNameAndRemovesWithoutForceWhenStopped()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteContainerTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteContainerMessage}|db", request.Message);
        Assert.Equal(ResourceKeys.DialogDeleteButton, request.PrimaryButtonText);
        Assert.Null(request.OptionText);
        Assert.Equal(["RemoveContainer c2 force=False", "ListContainers all=True"], docker.Calls);
        Assert.Equal(["c1", "c3"], Ids(viewModel));
        Assert.Empty(viewModel.SelectedContainers);
        Assert.Equal(0, viewModel.SelectionCount);
    }

    [Fact]
    public async Task Remove_UsesTheRunningMessageAndForcesWhenTheContainerIsRunning()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteContainerTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteRunningContainerMessage}|web", request.Message);
        Assert.Equal(["RemoveContainer c1 force=True", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Remove_OfSeveralStoppedContainersAsksOnceWithTheCount()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteContainersTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteContainersMessage}|2", request.Message);
        Assert.Equal(ResourceKeys.DialogDeleteButton, request.PrimaryButtonText);
        Assert.Equal(
            ["RemoveContainer c2 force=False", "RemoveContainer c3 force=False", "ListContainers all=True"],
            Sorted(docker.Calls, 2));
        Assert.Equal(["c1"], Ids(viewModel));
    }

    [Fact]
    public async Task Remove_OfSeveralNamesTheRunningOnesAndForcesPerContainer()
    {
        docker.Containers.Add(Container("c4", "cache", "paused"));
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2, 3);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteContainersTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteContainersRunningMessage}|4|2", request.Message);
        Assert.Equal(
            [
                "RemoveContainer c1 force=True",
                "RemoveContainer c2 force=False",
                "RemoveContainer c3 force=False",
                "RemoveContainer c4 force=True",
                "ListContainers all=True",
            ],
            Sorted(docker.Calls, 4));
        Assert.Empty(viewModel.Containers);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Remove_ReportsAPartialFailureAndStillReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailures["c2"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"container is busy"}""");
        docker.MutationFailures["c3"] = new InvalidOperationException("engine hiccup");
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(
            $"{ResourceKeys.BulkPartialFailure}|2|3\ndb: container is busy\njob: engine hiccup",
            viewModel.ErrorMessage);
        Assert.Equal(1, docker.Count("ListContainers"));
        Assert.Equal(["c2", "c3"], Ids(viewModel));
    }

    [Fact]
    public async Task Remove_WithASingleFailureAmongSeveralShowsJustThatMessage()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailures["c3"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"container is busy"}""");
        Select(viewModel, 0, 1, 2);

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal("container is busy", viewModel.ErrorMessage);
        Assert.Equal(["c3"], Ids(viewModel));
    }

    [Fact]
    public async Task Remove_AsksBeforeTheViewModelBecomesBusy()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        bool? busyWhileAsking = null;
        dialogs.Handler = _ =>
        {
            busyWhileAsking = viewModel.IsBusy;
            return Task.FromResult(new ConfirmResult(true, false));
        };

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.False(busyWhileAsking);
    }

    [Fact]
    public async Task UpdateSelection_CopiesTheGivenItems()
    {
        var viewModel = await LoadedViewModelAsync();
        var picked = new List<ContainerItem> { viewModel.Containers[0], viewModel.Containers[1] };

        viewModel.UpdateSelection(picked.Where(_ => true));
        picked.Clear();

        Assert.Equal(["c1", "c2"], viewModel.SelectedContainers.Select(item => item.Id));
        Assert.Equal(2, viewModel.SelectionCount);
    }

    [Fact]
    public async Task Remove_AsksAgainOnEveryInvocationWithTheScriptedAnswers()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1, 2);
        dialogs.Script(new ConfirmResult(false, false), new ConfirmResult(true, false));

        await viewModel.RemoveCommand.ExecuteAsync(null);
        Assert.Equal(3, viewModel.Containers.Count);

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogs.Requests.Count);
        Assert.Equal(["c1"], Ids(viewModel));
    }

    [Fact]
    public async Task Refresh_KeepsTheItemInstancesAndUpdatesTheirInfo()
    {
        var viewModel = await LoadedViewModelAsync();
        var before = viewModel.Containers.ToArray();
        var changed = new List<string?>();
        before[1].PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        docker.Containers[1] = docker.Containers[1] with { State = "running", Status = "Up 1 second" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(before, viewModel.Containers);
        Assert.Equal("running", before[1].Info.State);
        Assert.Equal("Up 1 second", before[1].Info.Status);
        Assert.Equal(["Info"], changed);
    }

    [Fact]
    public async Task Refresh_RaisesNoCollectionChangeWhenOnlyValuesChange()
    {
        var viewModel = await LoadedViewModelAsync();
        var events = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Containers.CollectionChanged += (_, e) => events.Add(e);
        docker.Containers[0] = docker.Containers[0] with { Status = "Up 2 hours" };

        await viewModel.RefreshCommand.ExecuteAsync(null);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(events);
    }

    [Fact]
    public async Task Refresh_InsertsNewContainersAndRemovesGoneOnesWithoutResetting()
    {
        var viewModel = await LoadedViewModelAsync();
        var before = viewModel.Containers.ToArray();
        var events = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Containers.CollectionChanged += (_, e) => events.Add(e);
        docker.Containers.RemoveAt(1);
        docker.Containers.Insert(0, Container("c0", "new", "running"));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["c0", "c1", "c3"], Ids(viewModel));
        Assert.Same(before[0], viewModel.Containers[1]);
        Assert.Same(before[2], viewModel.Containers[2]);
        Assert.DoesNotContain(events, e => e.Action == NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public async Task Refresh_KeepsTheSelectionOfTheSameContainer()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        var selected = viewModel.Containers[1];
        docker.Containers[1] = docker.Containers[1] with { State = "running", Status = "Up 1 second" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(selected, Assert.Single(viewModel.SelectedContainers));
        Assert.Same(selected, viewModel.Containers[1]);
        Assert.Equal("running", selected.Info.State);
        Assert.Equal(1, viewModel.SelectionCount);
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_DropsContainersThatAreGoneFromTheSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        docker.Containers.RemoveAt(1);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["c1", "c3"], viewModel.SelectedContainers.Select(item => item.Id));
        Assert.Equal(2, viewModel.SelectionCount);
        Assert.Contains(nameof(ContainersViewModel.SelectionCount), raised);

        docker.Containers.Clear();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SelectedContainers);
        Assert.Equal(0, viewModel.SelectionCount);
        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_ReevaluatesTheCommandsWhenTheStateOfASelectedContainerChanges()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.StartCommand.CanExecute(null));
        var raised = new List<string>();
        viewModel.StartCommand.CanExecuteChanged += (_, _) => raised.Add("start");
        viewModel.StopCommand.CanExecuteChanged += (_, _) => raised.Add("stop");
        docker.Containers[0] = docker.Containers[0] with { State = "exited", Status = "Exited (0)" };

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
        Assert.Contains(nameof(ContainersViewModel.IsEmpty), raised);

        docker.Containers.Add(Container("c1", "web", "running"));
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
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);

        docker.ListFailure = null;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(3, viewModel.Containers.Count);
    }

    [Fact]
    public async Task Action_ShowsTheDaemonMessageAndReloadsWhenTheEngineRejectsIt()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.MutationFailure = new DockerApiException(HttpStatusCode.Conflict, """{"message":"container is busy"}""");
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal("container is busy", viewModel.ErrorMessage);
        Assert.Equal(["Stop c1", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Action_DoesNotReloadWhenDockerBecameUnavailable()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.MutationFailure = new DockerUnavailableException("gone");
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.Equal(["Stop c1"], docker.Calls);
    }

    [Fact]
    public async Task Action_ClearsThePreviousErrorWhenItStarts()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        docker.MutationFailure = new InvalidOperationException("first");
        await viewModel.StopCommand.ExecuteAsync(null);
        Assert.Equal("first", viewModel.ErrorMessage);
        docker.MutationFailure = null;

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_IgnoresAnAnswerThatIsSupersededByANewerRefresh()
    {
        var viewModel = CreateViewModel();
        var older = new TaskCompletionSource<IReadOnlyList<ContainerInfo>>();
        var newer = new TaskCompletionSource<IReadOnlyList<ContainerInfo>>();
        var answers = new Queue<TaskCompletionSource<IReadOnlyList<ContainerInfo>>>([older, newer]);
        docker.ListContainersHandler = _ => answers.Dequeue().Task;

        var first = viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.ShowAll = false;
        newer.SetResult([Container("new", "new", "running")]);
        older.SetResult([Container("old", "old", "running")]);
        await first;

        Assert.Equal(["new"], Ids(viewModel));
    }

    [Fact]
    public async Task Refresh_ReportsBusyWhileTheEngineIsAsked()
    {
        var viewModel = CreateViewModel();
        var gate = new TaskCompletionSource<IReadOnlyList<ContainerInfo>>();
        docker.ListContainersHandler = _ => gate.Task;

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
        var before = viewModel.Containers.ToArray();
        var busyChanges = 0;
        viewModel.PropertyChanged += (_, e) => busyChanges += e.PropertyName == nameof(ContainersViewModel.IsBusy) ? 1 : 0;
        docker.Containers[1] = docker.Containers[1] with { State = "running", Status = "Up 3 seconds" };
        docker.Containers.Add(Container("c4", "cache", "running"));

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(["c1", "c2", "c3", "c4"], Ids(viewModel));
        Assert.Same(before[1], viewModel.Containers[1]);
        Assert.Equal("Up 3 seconds", before[1].Info.Status);
        Assert.Equal(0, busyChanges);
    }

    [Fact]
    public async Task RefreshQuietly_IsSkippedWhileTheDeleteConfirmationIsOpen()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        var answer = new TaskCompletionSource<ConfirmResult>();
        dialogs.Handler = _ => answer.Task;
        docker.Calls.Clear();

        var remove = viewModel.RemoveCommand.ExecuteAsync(null);
        await viewModel.RefreshQuietlyAsync();

        Assert.Empty(docker.Calls);

        answer.SetResult(new ConfirmResult(false, false));
        await remove;
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(["ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public Task AutoRefresh_KeepsTheListAndTheSelectionCurrent() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        var selected = viewModel.Containers[0];
        docker.Calls.Clear();

        viewModel.StartAutoRefresh();
        docker.Containers[0] = docker.Containers[0] with { State = "exited", Status = "Exited (0) 1 second ago" };
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.Equal(["ListContainers all=True"], docker.Calls);
        Assert.Same(selected, Assert.Single(viewModel.SelectedContainers));
        Assert.Equal("exited", selected.Info.State);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));

        docker.Containers.Add(Container("c4", "cache", "running"));
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.Equal(2, docker.Count("ListContainers"));
        Assert.Equal(["c1", "c2", "c3", "c4"], Ids(viewModel));
        Assert.False(viewModel.IsBusy);

        viewModel.StopAutoRefresh();
    });

    [Fact]
    public Task AutoRefresh_ShowsAnEngineErrorAndClearsItByItself() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.StartAutoRefresh();

        docker.ListFailure = new DockerUnavailableException("quit");
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.Equal(3, viewModel.Containers.Count);
        Assert.False(viewModel.IsBusy);

        docker.ListFailure = null;
        time.Advance(TimeSpan.FromSeconds(5));
        await Task.Yield();

        Assert.Null(viewModel.ErrorMessage);
        viewModel.StopAutoRefresh();
    });

    [Fact]
    public Task AutoRefresh_FollowsTheIntervalOfTheSettingsAndStopsWhenAsked() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        docker.Calls.Clear();
        viewModel.StartAutoRefresh();

        settings.RefreshInterval = TimeSpan.FromSeconds(2);
        await Task.Yield();
        time.Advance(TimeSpan.FromSeconds(2));
        await Task.Yield();

        Assert.Equal(1, docker.Count("ListContainers"));

        viewModel.StopAutoRefresh();
        time.Advance(TimeSpan.FromMinutes(5));
        await Task.Yield();
        await Task.Yield();

        Assert.Equal(1, docker.Count("ListContainers"));
    });

    [Fact]
    public async Task Prune_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        dialogs.Decline();
        docker.Calls.Clear();

        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
        Assert.Null(viewModel.StatusMessage);
        Assert.False(viewModel.HasStatus);
        Assert.False(viewModel.HasError);
        Assert.Equal(3, viewModel.Containers.Count);
    }

    [Fact]
    public async Task Prune_WarnsThenPrunesAndReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.PruneResult = new PruneResult(2, 12_300_000);
        docker.Containers.RemoveAt(2);
        docker.Containers.RemoveAt(1);
        docker.Calls.Clear();

        await viewModel.PruneCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmPruneContainersTitle, request.Title);
        Assert.Equal(ResourceKeys.ConfirmPruneContainersMessage, request.Message);
        Assert.Equal(ResourceKeys.DialogPruneButton, request.PrimaryButtonText);
        Assert.Null(request.OptionText);
        Assert.Equal(["PruneContainers", "ListContainers all=True"], docker.Calls);
        Assert.Equal(["c1"], Ids(viewModel));
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Prune_ReportsTheNumberOfRemovedContainersAndTheReclaimedSpace()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.PruneResult = new PruneResult(2, 12_300_000);

        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.PruneContainersResult}|2|12.3MB", viewModel.StatusMessage);
        Assert.True(viewModel.HasStatus);

        docker.PruneResult = new PruneResult(0, 0);
        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.PruneContainersResult}|0|0B", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Prune_NeedsNoSelection()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.PruneCommand.CanExecute(null));

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.PruneCommand.CanExecute(null));

        Select(viewModel, 0);
        Assert.True(viewModel.PruneCommand.CanExecute(null));
    }

    [Fact]
    public async Task Prune_KeepsTheSelectionOfContainersThatSurvive()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1);
        var survivor = viewModel.Containers[0];
        docker.Containers.RemoveAt(1);

        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Same(survivor, Assert.Single(viewModel.SelectedContainers));
        Assert.Equal(1, viewModel.SelectionCount);
    }

    [Fact]
    public async Task Prune_ShowsTheEnginesErrorAsTheErrorOfTheActionAndStillReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailure = new DockerApiException(HttpStatusCode.Conflict, """{"message":"a prune operation is already running"}""");
        docker.Calls.Clear();

        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Equal("a prune operation is already running", viewModel.ErrorMessage);
        Assert.Null(viewModel.StatusMessage);
        Assert.Equal(["PruneContainers", "ListContainers all=True"], docker.Calls);

        // Like any error of an action, it stays when a background refresh succeeds.
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("a prune operation is already running", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Prune_ShowsTheUnavailableTextAndDoesNotReloadWhenDockerBecameUnavailable()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailure = new DockerUnavailableException("gone");
        docker.Calls.Clear();

        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.Null(viewModel.StatusMessage);
        Assert.Equal(["PruneContainers"], docker.Calls);
    }

    [Fact]
    public async Task Prune_ReplacesTheMessagesOfTheActionBefore()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailure = new InvalidOperationException("first");
        await viewModel.PruneCommand.ExecuteAsync(null);
        Assert.Equal("first", viewModel.ErrorMessage);
        docker.MutationFailure = null;
        docker.PruneResult = new PruneResult(1, 1500);

        await viewModel.PruneCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal($"{ResourceKeys.PruneContainersResult}|1|1.5kB", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Prune_ConfirmationPausesTheBackgroundRefresh()
    {
        var viewModel = await LoadedViewModelAsync();
        var answer = new TaskCompletionSource<ConfirmResult>();
        dialogs.Handler = _ => answer.Task;
        docker.Calls.Clear();

        var prune = viewModel.PruneCommand.ExecuteAsync(null);
        await viewModel.RefreshQuietlyAsync();

        Assert.Empty(docker.Calls);

        answer.SetResult(new ConfirmResult(false, false));
        await prune;
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(["ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        var localizer = new FakeLocalizer();

        Assert.Throws<ArgumentNullException>(() => new ContainersViewModel(null!, dialogs, localizer, settings, time));
        Assert.Throws<ArgumentNullException>(() => new ContainersViewModel(docker, null!, localizer, settings, time));
        Assert.Throws<ArgumentNullException>(() => new ContainersViewModel(docker, dialogs, null!, settings, time));
        Assert.Throws<ArgumentNullException>(() => new ContainersViewModel(docker, dialogs, localizer, null!, time));
        Assert.Throws<ArgumentNullException>(() => new ContainersViewModel(docker, dialogs, localizer, settings, null!));
    }
}
