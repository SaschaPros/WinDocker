using System.Net;
using Docker.DotNet;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class ContainersViewModelTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeDockerService docker = new();
    private readonly FakeDialogService dialogs = new();

    public ContainersViewModelTests()
    {
        docker.Containers.Add(Container("c1", "web", "running"));
        docker.Containers.Add(Container("c2", "db", "exited"));
        docker.Containers.Add(Container("c3", "job", "created"));
    }

    private ContainersViewModel CreateViewModel() => new(docker, dialogs, new FakeLocalizer());

    private static ContainerInfo Container(string id, string name, string state) =>
        new(id, name, "image", "cmd", Created, state, state, string.Empty);

    private async Task<ContainersViewModel> LoadedViewModelAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task Refresh_LoadsAllContainersByDefault()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.True(viewModel.ShowAll);
        Assert.Equal(["c1", "c2", "c3"], viewModel.Containers.Select(container => container.Id));
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
        Assert.Equal(["c1"], viewModel.Containers.Select(container => container.Id));

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

        viewModel.SelectedContainer = viewModel.Containers[0];
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        viewModel.SelectedContainer = viewModel.Containers[1];
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        viewModel.SelectedContainer = viewModel.Containers[2];
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));

        viewModel.SelectedContainer = null;
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.StopCommand.CanExecute(null));
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectionChange_RaisesCanExecuteChangedOnAllActionCommands()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = new List<string>();
        viewModel.StartCommand.CanExecuteChanged += (_, _) => raised.Add("start");
        viewModel.StopCommand.CanExecuteChanged += (_, _) => raised.Add("stop");
        viewModel.RemoveCommand.CanExecuteChanged += (_, _) => raised.Add("remove");

        viewModel.SelectedContainer = viewModel.Containers[0];

        Assert.Equal(["remove", "start", "stop"], raised.Order());
    }

    [Fact]
    public async Task HasSelection_FollowsTheSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.False(viewModel.HasSelection);

        viewModel.SelectedContainer = viewModel.Containers[0];

        Assert.True(viewModel.HasSelection);
        Assert.Contains(nameof(ContainersViewModel.HasSelection), raised);

        viewModel.SelectedContainer = null;

        Assert.False(viewModel.HasSelection);
    }

    [Fact]
    public async Task Start_StartsTheSelectedContainerAndReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedContainer = viewModel.Containers[1];
        docker.Calls.Clear();

        await viewModel.StartCommand.ExecuteAsync(null);

        Assert.Equal(["Start c2", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Stop_StopsTheSelectedContainerAndReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedContainer = viewModel.Containers[0];
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(["Stop c1", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Remove_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedContainer = viewModel.Containers[1];
        dialogs.Answer = false;
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
        Assert.Equal(3, viewModel.Containers.Count);
    }

    [Fact]
    public async Task Remove_ConfirmsWithTheContainerNameAndRemovesWithoutForceWhenStopped()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedContainer = viewModel.Containers[1];
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteContainerTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteContainerMessage}|db", request.Message);
        Assert.Equal(["RemoveContainer c2 force=False", "ListContainers all=True"], docker.Calls);
        Assert.Equal(["c1", "c3"], viewModel.Containers.Select(container => container.Id));
        Assert.Null(viewModel.SelectedContainer);
    }

    [Fact]
    public async Task Remove_UsesTheRunningMessageAndForcesWhenTheContainerIsRunning()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedContainer = viewModel.Containers[0];
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteContainerTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteRunningContainerMessage}|web", request.Message);
        Assert.Equal(["RemoveContainer c1 force=True", "ListContainers all=True"], docker.Calls);
    }

    [Fact]
    public async Task Refresh_KeepsTheSelectionOfTheSameContainerById()
    {
        var viewModel = await LoadedViewModelAsync();
        var before = viewModel.Containers[1];
        viewModel.SelectedContainer = before;
        docker.Containers[1] = docker.Containers[1] with { State = "running", Status = "Up 1 second" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.SelectedContainer);
        Assert.Equal("c2", viewModel.SelectedContainer.Id);
        Assert.Equal("running", viewModel.SelectedContainer.State);
        Assert.NotSame(before, viewModel.SelectedContainer);
        Assert.Same(viewModel.Containers[1], viewModel.SelectedContainer);
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public async Task Refresh_ClearsTheSelectionWhenTheContainerIsGone()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedContainer = viewModel.Containers[1];
        docker.Containers.RemoveAt(1);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedContainer);
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
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
        viewModel.SelectedContainer = viewModel.Containers[0];
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
        viewModel.SelectedContainer = viewModel.Containers[0];
        docker.MutationFailure = new DockerUnavailableException("gone");
        docker.Calls.Clear();

        await viewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.Equal(["Stop c1"], docker.Calls);
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

        Assert.Equal(["new"], viewModel.Containers.Select(container => container.Id));
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
}
