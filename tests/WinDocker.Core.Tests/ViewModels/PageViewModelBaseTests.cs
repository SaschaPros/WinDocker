using System.Net;
using Docker.DotNet;
using WinDocker.Core.Localization;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class PageViewModelBaseTests
{
    private sealed class TestViewModel() : PageViewModelBase(new FakeLocalizer())
    {
        public Task RunAsync(Func<Task> action) => RunSafeAsync(action);
    }

    [Fact]
    public async Task RunSafeAsync_SetsBusyWhileTheActionRuns()
    {
        var viewModel = new TestViewModel();
        var gate = new TaskCompletionSource();

        var run = viewModel.RunAsync(() => gate.Task);

        Assert.True(viewModel.IsBusy);
        gate.SetResult();
        await run;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RunSafeAsync_StaysBusyUntilOverlappingActionsHaveAllFinished()
    {
        var viewModel = new TestViewModel();
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();

        var firstRun = viewModel.RunAsync(() => first.Task);
        var secondRun = viewModel.RunAsync(() => second.Task);
        first.SetResult();
        await firstRun;
        Assert.True(viewModel.IsBusy);

        second.SetResult();
        await secondRun;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RunSafeAsync_ClearsThePreviousError()
    {
        var viewModel = new TestViewModel();
        await viewModel.RunAsync(() => throw new InvalidOperationException("first"));
        Assert.Equal("first", viewModel.ErrorMessage);
        var gate = new TaskCompletionSource();

        var run = viewModel.RunAsync(() => gate.Task);

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        gate.SetResult();
        await run;
    }

    [Fact]
    public async Task RunSafeAsync_MapsDockerUnavailableToTheLocalizedText()
    {
        var viewModel = new TestViewModel();

        await viewModel.RunAsync(() => throw new DockerUnavailableException("pipe missing"));

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
    }

    [Fact]
    public async Task RunSafeAsync_ShowsTheDaemonMessageOfAnApiError()
    {
        var viewModel = new TestViewModel();
        var body = """{"message":"conflict: unable to delete abc (must be forced)"}""";

        await viewModel.RunAsync(() => throw new DockerApiException(HttpStatusCode.Conflict, body));

        Assert.Equal("conflict: unable to delete abc (must be forced)", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunSafeAsync_FallsBackToTheExceptionMessageForAnApiErrorWithoutBody()
    {
        var viewModel = new TestViewModel();
        var exception = new DockerApiException(HttpStatusCode.InternalServerError, null);

        await viewModel.RunAsync(() => throw exception);

        Assert.Equal(exception.Message, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunSafeAsync_ShowsTheMessageOfAnyOtherException()
    {
        var viewModel = new TestViewModel();

        await viewModel.RunAsync(() => throw new InvalidOperationException("something broke"));

        Assert.Equal("something broke", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunSafeAsync_IgnoresCancellation()
    {
        var viewModel = new TestViewModel();

        await viewModel.RunAsync(() => throw new OperationCanceledException());

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task HasError_SetToFalseDismissesTheError()
    {
        var viewModel = new TestViewModel();
        await viewModel.RunAsync(() => throw new InvalidOperationException("boom"));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.HasError = false;

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        Assert.Contains(nameof(TestViewModel.ErrorMessage), changed);
        Assert.Contains(nameof(TestViewModel.HasError), changed);
    }

    [Fact]
    public async Task HasError_SetToTrueDoesNothing()
    {
        var viewModel = new TestViewModel();

        viewModel.HasError = true;
        Assert.False(viewModel.HasError);

        await viewModel.RunAsync(() => throw new InvalidOperationException("boom"));
        viewModel.HasError = true;
        Assert.Equal("boom", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task HasError_RaisesChangeNotificationsWhenAnErrorAppears()
    {
        var viewModel = new TestViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.RunAsync(() => throw new InvalidOperationException("boom"));

        Assert.Contains(nameof(TestViewModel.ErrorMessage), changed);
        Assert.Contains(nameof(TestViewModel.HasError), changed);
    }
}
