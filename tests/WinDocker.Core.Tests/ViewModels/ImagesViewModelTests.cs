using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class ImagesViewModelTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeDockerService docker = new();
    private readonly FakeDialogService dialogs = new();

    public ImagesViewModelTests()
    {
        docker.Images.Add(new ImageInfo("sha256:aaaaaaaaaaaa1111", "nginx", "1.27", Created, 142_000_000));
        docker.Images.Add(new ImageInfo("sha256:aaaaaaaaaaaa1111", "nginx", "latest", Created, 142_000_000));
        docker.Images.Add(new ImageInfo("sha256:bbbbbbbbbbbb2222", "<none>", "<none>", Created, 7_830_000));
    }

    private async Task<ImagesViewModel> LoadedViewModelAsync()
    {
        var viewModel = new ImagesViewModel(docker, dialogs, new FakeLocalizer());
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task Refresh_LoadsTheImages()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(["nginx:1.27", "nginx:latest", "sha256:bbbbbbbbbbbb2222"], viewModel.Images.Select(image => image.Reference));
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RemoveCommand_NeedsASelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Assert.False(viewModel.RemoveCommand.CanExecute(null));

        viewModel.SelectedImage = viewModel.Images[0];

        Assert.True(viewModel.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Remove_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedImage = viewModel.Images[0];
        dialogs.Answer = false;
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task Remove_ConfirmsWithTheReferenceAndRemovesByReference()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedImage = viewModel.Images[1];
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteImageTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteImageMessage}|nginx:latest", request.Message);
        Assert.Equal(["RemoveImage nginx:latest", "ListImages"], docker.Calls);
        Assert.Equal(["nginx:1.27", "sha256:bbbbbbbbbbbb2222"], viewModel.Images.Select(image => image.Reference));
    }

    [Fact]
    public async Task Remove_UsesTheImageIdForUntaggedImagesAndConfirmsWithTheShortId()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedImage = viewModel.Images[2];
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.ConfirmDeleteImageMessage}|bbbbbbbbbbbb", dialogs.Requests[0].Message);
        Assert.Equal(["RemoveImage sha256:bbbbbbbbbbbb2222", "ListImages"], docker.Calls);
    }

    [Fact]
    public async Task Refresh_KeepsTheSelectionOfTheSameReference()
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedImage = viewModel.Images[1];
        docker.Images.Insert(0, new ImageInfo("sha256:cccccccccccc3333", "redis", "7", Created, 1));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("nginx:latest", viewModel.SelectedImage?.Reference);
        Assert.Same(viewModel.Images[2], viewModel.SelectedImage);
    }

    [Fact]
    public async Task IsEmpty_IsTrueAfterALoadWithoutImages()
    {
        docker.Images.Clear();

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
