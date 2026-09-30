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

    private ImagesViewModel CreateViewModel() =>
        new(docker, dialogs, new FakeLocalizer(), FakeSettingsStore.CreateService(), new FakeTimeProvider());

    private async Task<ImagesViewModel> LoadedViewModelAsync()
    {
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static void Select(ImagesViewModel viewModel, params int[] indexes) =>
        viewModel.UpdateSelection(indexes.Select(index => viewModel.Images[index]));

    private static string[] References(ImagesViewModel viewModel) => viewModel.Images.Select(item => item.Info.Reference).ToArray();

    private static string[] Sorted(IEnumerable<string> calls, int parallelCalls) =>
        [.. calls.Take(parallelCalls).Order(StringComparer.Ordinal), .. calls.Skip(parallelCalls)];

    [Fact]
    public async Task Refresh_LoadsTheImages()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(["nginx:1.27", "nginx:latest", "sha256:bbbbbbbbbbbb2222"], References(viewModel));
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Items_AreKeyedByIdRepositoryAndTag()
    {
        var viewModel = await LoadedViewModelAsync();

        Assert.Equal(new ImageKey("sha256:aaaaaaaaaaaa1111", "nginx", "latest"), viewModel.Images[1].Key);
    }

    [Fact]
    public async Task RemoveCommand_NeedsASelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Assert.False(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 0);
        Assert.True(viewModel.RemoveCommand.CanExecute(null));

        Select(viewModel, 0, 2);
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

        Select(viewModel, 0, 1);

        Assert.Equal(1, raised);
        Assert.Equal(2, viewModel.SelectionCount);
        Assert.True(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);
        Assert.Equal(["nginx:1.27", "nginx:latest"], viewModel.SelectedImages.Select(item => item.Info.Reference));
    }

    [Fact]
    public async Task Remove_DoesNothingWhenTheUserDeclines()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1);
        dialogs.Decline();
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Single(dialogs.Requests);
        Assert.Empty(docker.Calls);
    }

    [Fact]
    public async Task Remove_ConfirmsWithTheReferenceAndRemovesByReference()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteImageTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteImageMessage}|nginx:latest", request.Message);
        Assert.Equal(ResourceKeys.DialogDeleteButton, request.PrimaryButtonText);
        Assert.Equal(["RemoveImage nginx:latest", "ListImages"], docker.Calls);
        Assert.Equal(["nginx:1.27", "sha256:bbbbbbbbbbbb2222"], References(viewModel));
        Assert.Empty(viewModel.SelectedImages);
    }

    [Fact]
    public async Task Remove_UsesTheImageIdForUntaggedImagesAndConfirmsWithTheShortId()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal($"{ResourceKeys.ConfirmDeleteImageMessage}|bbbbbbbbbbbb", dialogs.Requests[0].Message);
        Assert.Equal(["RemoveImage sha256:bbbbbbbbbbbb2222", "ListImages"], docker.Calls);
    }

    [Fact]
    public async Task Remove_OfSeveralAsksOnceWithTheCountAndReloadsOnce()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        var request = Assert.Single(dialogs.Requests);
        Assert.Equal(ResourceKeys.ConfirmDeleteImagesTitle, request.Title);
        Assert.Equal($"{ResourceKeys.ConfirmDeleteImagesMessage}|3", request.Message);
        Assert.Equal(ResourceKeys.DialogDeleteButton, request.PrimaryButtonText);
        Assert.Equal(
            ["RemoveImage nginx:1.27", "RemoveImage nginx:latest", "RemoveImage sha256:bbbbbbbbbbbb2222", "ListImages"],
            Sorted(docker.Calls, 3));
        Assert.Empty(viewModel.Images);
        Assert.True(viewModel.IsEmpty);
    }

    [Fact]
    public async Task Remove_ReportsAPartialFailureWithTheImageNames()
    {
        var viewModel = await LoadedViewModelAsync();
        docker.MutationFailures["nginx:1.27"] = new DockerApiException(HttpStatusCode.Conflict, """{"message":"image is being used"}""");
        docker.MutationFailures["sha256:bbbbbbbbbbbb2222"] = new InvalidOperationException("boom");
        Select(viewModel, 0, 1, 2);
        docker.Calls.Clear();

        await viewModel.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(
            $"{ResourceKeys.BulkPartialFailure}|2|3\nnginx:1.27: image is being used\nbbbbbbbbbbbb: boom",
            viewModel.ErrorMessage);
        Assert.Equal(1, docker.Count("ListImages"));
        Assert.Equal(["nginx:1.27", "sha256:bbbbbbbbbbbb2222"], References(viewModel));
    }

    [Fact]
    public async Task Refresh_KeepsTheItemInstancesAndTheSelectionOfTheSameRow()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 1);
        var selected = viewModel.Images[1];
        var events = new List<NotifyCollectionChangedEventArgs>();
        viewModel.Images.CollectionChanged += (_, e) => events.Add(e);
        docker.Images.Insert(0, new ImageInfo("sha256:cccccccccccc3333", "redis", "7", Created, 1));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["redis:7", "nginx:1.27", "nginx:latest", "sha256:bbbbbbbbbbbb2222"], References(viewModel));
        Assert.Same(selected, viewModel.Images[2]);
        Assert.Same(selected, Assert.Single(viewModel.SelectedImages));
        Assert.All(events, e => Assert.Equal(NotifyCollectionChangedAction.Add, e.Action));
    }

    [Fact]
    public async Task Refresh_TreatsTheRowsOfOneImageAsSeparateItems()
    {
        var viewModel = await LoadedViewModelAsync();
        var oneDotTwentySeven = viewModel.Images[0];
        var latest = viewModel.Images[1];
        docker.Images[1] = docker.Images[1] with { Tag = "stable" };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["nginx:1.27", "nginx:stable", "sha256:bbbbbbbbbbbb2222"], References(viewModel));
        Assert.Same(oneDotTwentySeven, viewModel.Images[0]);
        Assert.NotSame(latest, viewModel.Images[1]);
    }

    [Fact]
    public async Task Refresh_UpdatesTheInfoOfARowInPlace()
    {
        var viewModel = await LoadedViewModelAsync();
        var item = viewModel.Images[0];
        docker.Images[0] = docker.Images[0] with { SizeBytes = 1 };

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Same(item, viewModel.Images[0]);
        Assert.Equal(1, item.Info.SizeBytes);
    }

    [Fact]
    public async Task Refresh_DropsRemovedImagesFromTheSelection()
    {
        var viewModel = await LoadedViewModelAsync();
        Select(viewModel, 0, 2);
        docker.Images.RemoveAt(2);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["nginx:1.27"], viewModel.SelectedImages.Select(item => item.Info.Reference));
        Assert.Equal(1, viewModel.SelectionCount);
        Assert.True(viewModel.HasSingleSelection);
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
