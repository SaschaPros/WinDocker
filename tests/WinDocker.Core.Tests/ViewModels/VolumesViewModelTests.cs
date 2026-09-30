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
    }

    private async Task<VolumesViewModel> LoadedViewModelAsync()
    {
        var viewModel = new VolumesViewModel(docker, dialogs, new FakeLocalizer());
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task Refresh_LoadsTheVolumes()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(["data", "logs"], viewModel.Volumes.Select(volume => volume.Name));
        Assert.False(viewModel.IsEmpty);
    }

    [Fact]
    public async Task RemoveCommand_NeedsASelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Assert.False(viewModel.RemoveCommand.CanExecute(null));

        viewModel.SelectedVolume = viewModel.Volumes[0];

        Assert.True(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Remove_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedVolume = viewModel.Volumes[0];
        dialogs.Answer = false;
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task Remove_ConfirmsWithTheNameRemovesAndReloads()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedVolume = viewModel.Volumes[1];
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteVolumeTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteVolumeMessage}|logs", request.Message);
        Assert.Equal(["RemoveVolume logs", "ListVolumes"], docker.Calls);
        Assert.Equal(["data"], viewModel.Volumes.Select(volume => volume.Name));
        Assert.Null(viewModel.SelectedVolume);
    }

    [Fact]
    public async Task Refresh_KeepsTheSelectionOfTheSameName()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedVolume = viewModel.Volumes[1];
        docker.Volumes.Insert(0, new VolumeInfo("aaa", "local", "/x", null));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("logs", viewModel.SelectedVolume?.Name);
        Assert.Same(viewModel.Volumes[2], viewModel.SelectedVolume);
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
