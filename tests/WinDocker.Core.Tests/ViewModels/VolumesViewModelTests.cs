using System.Collections.Specialized;
using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Time.Testing;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class VolumesViewModelTests
{
    private readonly FakeDockerService docker = new();
    private readonly FakeDialogService dialogs = new();

    public VolumesViewModelTests()
    {
        docker.Volumes.Add(new VolumeInfo("data", "local", "/var/lib/docker/volumes/data/_data", null));
        docker.Volumes.Add(new VolumeInfo("logs", "local", "/var/lib/docker/volumes/logs/_data", null));
        docker.Volumes.Add(new VolumeInfo("cache", "local", "/var/lib/docker/volumes/cache/_data", null));
    }

    private VolumesViewModel CreateViewModel() =>
        new(docker, dialogs, new FakeLocalizer(), FakeSettingsStore.CreateService(), new FakeTimeProvider());

    private async Task<VolumesViewModel> LoadedViewModelAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static void Select(VolumesViewModel viewModel, params int[] indexes) =>
        viewModel.UpdateSelection(indexes.Select(index => viewModel.Volumes[index]));

    private static string[] Names(VolumesViewModel viewModel) => viewModel.Volumes.Select(item => item.Name).ToArray();

    private static string[] Sorted(IEnumerable<string> calls, int parallelCalls) =>
        [.. calls.Take(parallelCalls).Order(StringComparer.Ordinal), .. calls.Skip(parallelCalls)];

    [Fact]
    public async Task Refresh_LoadsTheVolumes()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(["data", "logs", "cache"], Names(viewModel));
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task RemoveCommand_NeedsASelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Assert.False(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 0);
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 0, 1);
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        viewModel.UpdateSelection([]);
        Assert.False(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selection_RaisesCanExecuteChangedAndExposesTheCount()
    {
        var viewModel = await LoadedViewModelAsync();
        var raised = 0;
        viewModel.RemoveCommand.CanExecuteChanged += (_, _) => raised++;

        Select(viewModel, 0);

        Assert.Equal(1, raised);
        Assert.Equal(1, viewModel.SelectionCount);
        Assert.True(viewModel.HasSingleSelection);
        Assert.Equal(["data"], viewModel.SelectedVolumes.Select(item => item.Name));
    }

    [Fact]
    public async Task Remove_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0);
        dialogs.Decline();
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task Remove_ConfirmsWithTheNameRemovesAndReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteVolumeTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteVolumeMessage}|logs", request.Message);
        Assert.Equal(ResourceKeys.DialogDeleteButton, request.PrimaryButtonText);
        Assert.Equal(["RemoveVolume logs", "ListVolumes"], docker.Calls);
        Assert.Equal(["data", "cache"], Names(viewModel));
        Assert.Empty(viewModel.SelectedVolumes);
    }

    [Fact]
    public async Task Remove_OfSeveralAsksOnceWithTheCountAndReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteVolumesTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteVolumesMessage}|2", request.Message);
        Assert.Equal(ResourceKeys.DialogDeleteButton, request.PrimaryButtonText);
        Assert.Equal(["RemoveVolume cache", "RemoveVolume data", "ListVolumes"], Sorted(docker.Calls, 2));
        Assert.Equal(["logs"], Names(viewModel));
    }

    [Fact]
    public async Task Remove_ReportsAPartialFailureWithTheVolumeNames()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailures["data"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"volume is in use"}""");
        docker.MutationFailures["cache"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"volume is in use too"}""");
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(
            $"{ResourceKeys.BulkPartialFailure}|2|3\ndata: volume is in use\ncache: volume is in use too",
            viewModel.ErrorMessage);
        Assert.Equal(1, docker.Count("ListVolumes"));
        Assert.Equal(["data", "cache"], Names(viewModel));
    }

    [Fact]
    public async Task Refresh_KeepsTheItemInstancesAndTheSelectionOfTheSameName()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        var selected = viewModel.Volumes[1];
        var events = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Volumes.CollectionChanged += (_, e) => events.Add(e);
        docker.Volumes.Insert(0, new VolumeInfo("aaa", "local", "/x", null));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["aaa", "data", "logs", "cache"], Names(viewModel));
        Assert.Same(selected, viewModel.Volumes[2]);
        Assert.Same(selected, Assert.Single(viewModel.SelectedVolumes));
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Add, e.Action));
    }

    [Fact]
    public async Task Refresh_UpdatesTheInfoOfARowInPlace()
    {
        var viewModel = await LoadedViewModelAsync();
        var item = viewModel.Volumes[0];
        docker.Volumes[0] = docker.Volumes[0] with { Mountpoint = "/somewhere/else" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(item, viewModel.Volumes[0]);
        Assert.Equal("/somewhere/else", item.Info.Mountpoint);
    }

    [Fact]
    public async Task Refresh_DropsRemovedVolumesFromTheSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1);
        docker.Volumes.RemoveAt(1);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["data"], viewModel.SelectedVolumes.Select(item => item.Name));
        Assert.Equal(1, viewModel.SelectionCount);
    }

    [Fact]
    public async Task IsEmpty_IsTrueAfterALoadWithoutVolumes()
    {
        docker.Volumes.Clear();

        var viewModel = await LoadedViewModelAsync();

        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Refresh_ShowsALocalizedErrorWhenDockerIsUnavailable()
    {
        docker.ListFailure = new DockerUnavailableException("down");

        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.False(viewModel.IsEmpty);
    }
}
