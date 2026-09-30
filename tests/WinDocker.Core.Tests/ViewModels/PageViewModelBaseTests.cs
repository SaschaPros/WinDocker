using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Time.Testing;
using WinDocker.Core.Localization;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class PageViewModelBaseTests
{
    private sealed class TestViewModel : PageViewModelBase
    {
        public TestViewModel()
            : base(new FakeLocalizer())
        {
        }

        public TestViewModel(SettingsService settings, TimeProvider timeProvider)
            : base(new FakeLocalizer(), settings, timeProvider)
        {
        }

        public Func<Task> OnRefresh { get; set; } = () => Task.CompletedTask;

        public int RefreshCalls { get; private set; }

        public bool Confirming => IsConfirming;

        public Task RunAsync(Func<Task> action) => RunSafeAsync(action);

        public Task ManualRefreshAsync() => RunRefreshAsync(RefreshCoreAsync);

        public Task MutateAsync(Func<Task> mutation, Func<Task> reload) => RunMutationAsync(mutation, reload);

        public Task BulkAsync<T>(IReadOnlyList<T> targets, Func<T, Task> action, Func<T, string> displayName, Func<Task> reload) =>
            RunBulkAsync(targets, action, displayName, reload);

        public Task<ConfirmResult> AskAsync(IDialogService dialogs, ConfirmRequest request) => ConfirmAsync(dialogs, request);

        public void SetStatus(string? text) => StatusMessage = text;

        public void SetError(string? text) => ErrorMessage = text;

        public void SetSelection(int count) => SetSelectionCount(count);

        protected override Task RefreshCoreAsync()
        {
            RefreshCalls++;
            return OnRefresh();
        }
    }

    private static TestViewModel CreateListViewModel(FakeTimeProvider? time = null, int refreshSeconds = 5) =>
        new(FakeSettingsStore.CreateService(refreshSeconds), time ?? new FakeTimeProvider());

    private static DockerApiException ApiError(string message) =>
        new(HttpStatusCode.Conflict, $$"""{"message":"{{message}}"}""");

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
    public async Task RunSafeAsync_ClearsThePreviousStatusMessage()
    {
        var viewModel = new TestViewModel();
        viewModel.SetStatus("Removed 3 containers");
        var gate = new TaskCompletionSource();

        var run = viewModel.RunAsync(() => gate.Task);

        Assert.Null(viewModel.StatusMessage);
        Assert.False(viewModel.HasStatus);
        gate.SetResult();
        await run;
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

    [Fact]
    public void StatusMessage_IsEmptyAtFirstAndHasStatusFollowsIt()
    {
        var viewModel = new TestViewModel();
        Assert.Null(viewModel.StatusMessage);
        Assert.False(viewModel.HasStatus);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.SetStatus("Removed 2 volumes");

        Assert.Equal("Removed 2 volumes", viewModel.StatusMessage);
        Assert.True(viewModel.HasStatus);
        Assert.Contains(nameof(TestViewModel.StatusMessage), changed);
        Assert.Contains(nameof(TestViewModel.HasStatus), changed);
    }

    [Fact]
    public void HasStatus_SetToFalseDismissesTheMessageAndSetToTrueDoesNothing()
    {
        var viewModel = new TestViewModel();
        viewModel.HasStatus = true;
        Assert.False(viewModel.HasStatus);
        viewModel.SetStatus("done");

        viewModel.HasStatus = true;
        Assert.Equal("done", viewModel.StatusMessage);

        viewModel.HasStatus = false;

        Assert.Null(viewModel.StatusMessage);
        Assert.False(viewModel.HasStatus);
    }

    [Fact]
    public void Selection_StartsEmptyAndSetSelectionCountUpdatesTheFlags()
    {
        var viewModel = new TestViewModel();
        Assert.Equal(0, viewModel.SelectionCount);
        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.SetSelection(1);

        Assert.Equal(1, viewModel.SelectionCount);
        Assert.True(viewModel.HasSelection);
        Assert.True(viewModel.HasSingleSelection);
        Assert.Contains(nameof(TestViewModel.SelectionCount), changed);
        Assert.Contains(nameof(TestViewModel.HasSelection), changed);
        Assert.Contains(nameof(TestViewModel.HasSingleSelection), changed);

        viewModel.SetSelection(3);

        Assert.True(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);

        viewModel.SetSelection(0);

        Assert.False(viewModel.HasSelection);
        Assert.False(viewModel.HasSingleSelection);
    }

    [Fact]
    public async Task RunMutationAsync_RunsTheMutationAndThenReloads()
    {
        var viewModel = new TestViewModel();
        var steps = new List<string>();

        await viewModel.MutateAsync(
            () =>
            {
                steps.Add("mutate");
                return Task.CompletedTask;
            },
            () =>
            {
                steps.Add("reload");
                return Task.CompletedTask;
            });

        Assert.Equal(["mutate", "reload"], steps);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task RunMutationAsync_ReloadsAndKeepsTheEnginesMessageWhenTheEngineRejectsTheChange()
    {
        var viewModel = new TestViewModel();
        var reloaded = false;

        await viewModel.MutateAsync(
            () => throw ApiError("container is busy"),
            () =>
            {
                reloaded = true;
                return Task.CompletedTask;
            });

        Assert.True(reloaded);
        Assert.Equal("container is busy", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunMutationAsync_KeepsTheOriginalErrorWhenTheReloadFailsToo()
    {
        var viewModel = new TestViewModel();

        await viewModel.MutateAsync(() => throw ApiError("container is busy"), () => throw new InvalidOperationException("reload failed"));

        Assert.Equal("container is busy", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunMutationAsync_DoesNotReloadWhenDockerBecameUnavailable()
    {
        var viewModel = new TestViewModel();
        var reloaded = false;

        await viewModel.MutateAsync(
            () => throw new DockerUnavailableException("gone"),
            () =>
            {
                reloaded = true;
                return Task.CompletedTask;
            });

        Assert.False(reloaded);
        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_RunsEveryTargetAndReloadsOnceAfterwards()
    {
        var viewModel = new TestViewModel();
        var done = new List<int>();
        var reloadsAfterAllActions = 0;
        var reloads = 0;

        await viewModel.BulkAsync(
            [1, 2, 3, 4, 5, 6],
            target =>
            {
                lock (done)
                {
                    done.Add(target);
                }

                return Task.CompletedTask;
            },
            target => target.ToString(CultureInfo.InvariantCulture),
            () =>
            {
                reloads++;
                reloadsAfterAllActions += done.Count;
                return Task.CompletedTask;
            });

        Assert.Equal([1, 2, 3, 4, 5, 6], done.Order());
        Assert.Equal(1, reloads);
        Assert.Equal(6, reloadsAfterAllActions);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RunBulkAsync_RunsAtMostFourActionsAtATime()
    {
        var viewModel = new TestViewModel();
        var release = new TaskCompletionSource();
        var fourRunning = new TaskCompletionSource();
        var running = 0;
        var peak = 0;
        var started = 0;

        var bulk = viewModel.BulkAsync(
            Enumerable.Range(0, 12).ToList(),
            async _ =>
            {
                var now = Interlocked.Increment(ref running);
                int seen;
                while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen)
                {
                }

                if (Interlocked.Increment(ref started) >= 4)
                {
                    fourRunning.TrySetResult();
                }

                await release.Task;
                Interlocked.Decrement(ref running);
            },
            target => target.ToString(CultureInfo.InvariantCulture),
            () => Task.CompletedTask);

        await fourRunning.Task.Within();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        release.SetResult();
        await bulk.Within();

        Assert.Equal(4, peak);
        Assert.Equal(12, started);
    }

    [Fact]
    public Task RunBulkAsync_RunsTheActionsOffTheCallersContextAndReloadsOnIt() => UiThread.RunAsync(async ui =>
    {
        var viewModel = new TestViewModel();
        var actionThreads = new System.Collections.Concurrent.ConcurrentBag<int>();
        var reloadThread = 0;

        await viewModel.BulkAsync(
            Enumerable.Range(0, 8).ToList(),
            _ =>
            {
                actionThreads.Add(Environment.CurrentManagedThreadId);
                return Task.CompletedTask;
            },
            target => target.ToString(CultureInfo.InvariantCulture),
            () =>
            {
                reloadThread = Environment.CurrentManagedThreadId;
                return Task.CompletedTask;
            }).Within();

        Assert.Equal(8, actionThreads.Count);
        Assert.DoesNotContain(ui.ThreadId, actionThreads);
        Assert.Equal(ui.ThreadId, reloadThread);
        Assert.False(viewModel.IsBusy);
    });

    [Fact]
    public async Task RunBulkAsync_IsBusyWhileTheActionsRunAndClearsTheOldError()
    {
        var viewModel = new TestViewModel();
        viewModel.SetError("old");
        var gate = new TaskCompletionSource();

        var bulk = viewModel.BulkAsync([1], _ => gate.Task, _ => "1", () => Task.CompletedTask);

        Assert.True(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
        gate.SetResult();
        await bulk;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RunBulkAsync_WithoutTargetsDoesNothing()
    {
        var viewModel = new TestViewModel();
        viewModel.SetError("keep");
        var touched = false;

        await viewModel.BulkAsync(
            Array.Empty<int>(),
            _ =>
            {
                touched = true;
                return Task.CompletedTask;
            },
            _ => string.Empty,
            () =>
            {
                touched = true;
                return Task.CompletedTask;
            });

        Assert.False(touched);
        Assert.Equal("keep", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RunBulkAsync_ShowsASingleFailureLikeAnyOtherError()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(
            ["a", "b", "c"],
            target => target == "b" ? throw ApiError("b is busy") : Task.CompletedTask,
            target => target,
            () => Task.CompletedTask);

        Assert.Equal("b is busy", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_ShowsASingleNonDockerFailureWithItsMessage()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(["a"], _ => throw new InvalidOperationException("nope"), target => target, () => Task.CompletedTask);

        Assert.Equal("nope", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_ShowsTheCountsAndTheFirstThreeFailuresInTargetOrder()
    {
        var viewModel = new TestViewModel();
        var reloads = 0;

        await viewModel.BulkAsync(
            ["a", "b", "c", "d", "e", "f"],
            target => target switch
            {
                "a" => Task.CompletedTask,
                "b" => throw ApiError("b is busy"),
                "c" => throw new InvalidOperationException("c broke"),
                "d" => throw ApiError("d is busy"),
                "e" => throw ApiError("e is busy"),
                _ => Task.CompletedTask,
            },
            target => target.ToUpperInvariant(),
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        Assert.Equal($"{ResourceKeys.BulkPartialFailure}|4|6\nB: b is busy\nC: c broke\nD: d is busy", viewModel.ErrorMessage);
        Assert.Equal(1, reloads);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RunBulkAsync_TwoFailuresListBoth()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(
            ["a", "b"],
            target => throw new InvalidOperationException($"{target} failed"),
            target => target,
            () => Task.CompletedTask);

        Assert.Equal($"{ResourceKeys.BulkPartialFailure}|2|2\na: a failed\nb: b failed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_CollectsFailuresFromParallelActionsWithoutLosingAny()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(
            Enumerable.Range(0, 400).ToList(),
            async target =>
            {
                await Task.Yield();
                if (target % 2 == 0)
                {
                    throw new InvalidOperationException($"{target} failed");
                }
            },
            target => $"#{target}",
            () => Task.CompletedTask);

        Assert.Equal($"{ResourceKeys.BulkPartialFailure}|200|400\n#0: 0 failed\n#2: 2 failed\n#4: 4 failed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_TreatsAnActionThatThrowsBeforeReturningATaskAsAFailure()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(
            ["a", "b"],
            target => target == "a" ? throw new InvalidOperationException("sync failure") : Task.CompletedTask,
            target => target,
            () => Task.CompletedTask);

        Assert.Equal("sync failure", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_DoesNotCountCancellationAsAFailure()
    {
        var viewModel = new TestViewModel();
        var reloads = 0;

        await viewModel.BulkAsync(
            ["a", "b"],
            target => target == "a" ? throw new OperationCanceledException() : Task.CompletedTask,
            target => target,
            () =>
            {
                reloads++;
                return Task.CompletedTask;
            });

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(1, reloads);
    }

    [Fact]
    public async Task RunBulkAsync_ReportsOnlyTheUnavailableTextAndSkipsTheReloadWhenTheEngineIsGone()
    {
        var viewModel = new TestViewModel();
        var reloaded = false;

        await viewModel.BulkAsync(
            ["a", "b", "c"],
            target => target switch
            {
                "a" => throw ApiError("a is busy"),
                "b" => throw new DockerUnavailableException("gone"),
                _ => Task.CompletedTask,
            },
            target => target,
            () =>
            {
                reloaded = true;
                return Task.CompletedTask;
            });

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.False(reloaded);
    }

    [Fact]
    public async Task RunBulkAsync_ReportsAFailedReloadWhenEveryActionSucceeded()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(["a"], _ => Task.CompletedTask, target => target, () => throw new DockerUnavailableException("gone"));

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_KeepsTheActionFailuresWhenTheReloadFailsToo()
    {
        var viewModel = new TestViewModel();

        await viewModel.BulkAsync(
            ["a", "b"],
            target => target == "b" ? throw ApiError("b is busy") : Task.CompletedTask,
            target => target,
            () => throw new InvalidOperationException("reload failed"));

        Assert.Equal("b is busy", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RunBulkAsync_TheErrorCountsAsTheErrorOfAnAction()
    {
        var viewModel = CreateListViewModel();
        await viewModel.BulkAsync(["a"], _ => throw new InvalidOperationException("nope"), target => target, () => Task.CompletedTask);

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("nope", viewModel.ErrorMessage);
    }

    [Fact]
    public void RunBulkAsync_RejectsMissingArguments()
    {
        var viewModel = new TestViewModel();
        IReadOnlyList<int> targets = [1];

        Assert.Throws<ArgumentNullException>(() => { _ = viewModel.BulkAsync<int>(null!, _ => Task.CompletedTask, _ => "", () => Task.CompletedTask); });
        Assert.Throws<ArgumentNullException>(() => { _ = viewModel.BulkAsync(targets, null!, _ => "", () => Task.CompletedTask); });
        Assert.Throws<ArgumentNullException>(() => { _ = viewModel.BulkAsync(targets, _ => Task.CompletedTask, null!, () => Task.CompletedTask); });
        Assert.Throws<ArgumentNullException>(() => { _ = viewModel.BulkAsync(targets, _ => Task.CompletedTask, _ => "", null!); });
    }

    [Fact]
    public async Task RefreshQuietly_RefreshesWithoutTouchingIsBusy()
    {
        var viewModel = CreateListViewModel();
        var busyStates = new List<bool>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageViewModelBase.IsBusy))
            {
                busyStates.Add(viewModel.IsBusy);
            }
        };
        bool? busyDuringRefresh = null;
        viewModel.OnRefresh = () =>
        {
            busyDuringRefresh = viewModel.IsBusy;
            return Task.CompletedTask;
        };

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(1, viewModel.RefreshCalls);
        Assert.False(busyDuringRefresh);
        Assert.Empty(busyStates);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RefreshQuietly_IsSkippedWhileAnActionRuns()
    {
        var viewModel = CreateListViewModel();
        var gate = new TaskCompletionSource();
        var action = viewModel.RunAsync(() => gate.Task);

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(0, viewModel.RefreshCalls);
        gate.SetResult();
        await action;

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(1, viewModel.RefreshCalls);
    }

    [Fact]
    public async Task RefreshQuietly_IsSkippedWhileAConfirmationIsOpen()
    {
        var viewModel = CreateListViewModel();
        var dialogs = new FakeDialogService();
        var answer = new TaskCompletionSource<ConfirmResult>();
        dialogs.Handler = _ => answer.Task;

        var asking = viewModel.AskAsync(dialogs, new ConfirmRequest("t", "m", "ok"));

        Assert.True(viewModel.Confirming);
        Assert.False(viewModel.IsBusy);
        await viewModel.RefreshQuietlyAsync();
        Assert.Equal(0, viewModel.RefreshCalls);

        answer.SetResult(new ConfirmResult(true, true));
        Assert.Equal(new ConfirmResult(true, true), await asking);
        Assert.False(viewModel.Confirming);

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(1, viewModel.RefreshCalls);
    }

    [Fact]
    public async Task Confirm_PassesTheRequestThroughAndEndsTheConfirmationWhenTheDialogFails()
    {
        var viewModel = CreateListViewModel();
        var dialogs = new FakeDialogService { Handler = _ => Task.FromException<ConfirmResult>(new InvalidOperationException("no window")) };
        var request = new ConfirmRequest("Title", "Message", "Delete", "Also volumes");

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.AskAsync(dialogs, request));

        Assert.Same(request, Assert.Single(dialogs.Requests));
        Assert.False(viewModel.Confirming);
        await viewModel.RefreshQuietlyAsync();
        Assert.Equal(1, viewModel.RefreshCalls);
    }

    [Fact]
    public async Task RefreshQuietly_ReportsAFailureAsARefreshError()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RefreshQuietly_SuccessClearsTheErrorOfAnEarlierRefresh()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        Assert.True(viewModel.HasError);
        viewModel.OnRefresh = () => Task.CompletedTask;

        await viewModel.RefreshQuietlyAsync();

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task RefreshQuietly_FailureReplacesTheErrorOfAnEarlierRefresh()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();

        viewModel.OnRefresh = () => throw new InvalidOperationException("another problem");
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("another problem", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_NeverReplacesTheErrorOfAnAction()
    {
        var viewModel = CreateListViewModel();
        await viewModel.RunAsync(() => throw new InvalidOperationException("action failed"));
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("action failed", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_SuccessKeepsTheErrorOfAnAction()
    {
        var viewModel = CreateListViewModel();
        await viewModel.RunAsync(() => throw new InvalidOperationException("action failed"));

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("action failed", viewModel.ErrorMessage);
        Assert.Equal(1, viewModel.RefreshCalls);
    }

    [Fact]
    public async Task RefreshQuietly_KeepsAnErrorThatWasSetByTheDerivedClass()
    {
        var viewModel = CreateListViewModel();
        viewModel.SetError("plain error");

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("plain error", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_ReportsAFailureAgainOnceTheActionErrorWasDismissed()
    {
        var viewModel = CreateListViewModel();
        await viewModel.RunAsync(() => throw new InvalidOperationException("action failed"));
        viewModel.HasError = false;
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_DoesNotBringBackADismissedRefreshErrorWhileItKeepsFailingTheSameWay()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        Assert.True(viewModel.HasError);

        viewModel.HasError = false;
        await viewModel.RefreshQuietlyAsync();
        await viewModel.RefreshQuietlyAsync();

        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.HasError);
        Assert.Equal(3, viewModel.RefreshCalls);
    }

    [Fact]
    public async Task RefreshQuietly_ShowsADifferentFailureAfterADismissal()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        viewModel.HasError = false;

        viewModel.OnRefresh = () => throw new InvalidOperationException("another problem");
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("another problem", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_ShowsTheSameFailureAgainOnceARefreshSucceededInBetween()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        viewModel.HasError = false;
        viewModel.OnRefresh = () => Task.CompletedTask;
        await viewModel.RefreshQuietlyAsync();

        viewModel.OnRefresh = () => throw new DockerUnavailableException("down again");
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ManualRefresh_ShowsItsFailureEvenAfterTheSameErrorWasDismissed()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        viewModel.HasError = false;

        await viewModel.ManualRefreshAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ARefreshErrorDismissedAfterAManualRefreshStaysDismissedForBackgroundRefreshes()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.ManualRefreshAsync();
        viewModel.HasError = false;

        await viewModel.RefreshQuietlyAsync();

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AnActionEndsTheSuppressionOfADismissedRefreshError()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        viewModel.HasError = false;

        await viewModel.RunAsync(() => Task.CompletedTask);
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task HasError_SetToFalseWithoutAnErrorDoesNotSuppressTheNextFailure()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();
        viewModel.OnRefresh = () => Task.CompletedTask;
        await viewModel.RefreshQuietlyAsync();
        Assert.False(viewModel.HasError);

        viewModel.HasError = false;
        viewModel.OnRefresh = () => throw new DockerUnavailableException("down");
        await viewModel.RefreshQuietlyAsync();

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_SuccessDoesNotClearTheErrorOfAManualRefreshThatFailedMeanwhile()
    {
        var viewModel = CreateListViewModel();
        var quietAnswer = new TaskCompletionSource();
        var answers = new Queue<Func<Task>>([() => quietAnswer.Task, () => throw new DockerUnavailableException("down")]);
        viewModel.OnRefresh = () => answers.Dequeue()();

        var quiet = viewModel.RefreshQuietlyAsync();
        await viewModel.ManualRefreshAsync();
        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        quietAnswer.SetResult();
        await quiet;

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_FailureIsDroppedWhenAnActionRanMeanwhile()
    {
        var viewModel = CreateListViewModel();
        var quietAnswer = new TaskCompletionSource();
        viewModel.OnRefresh = () => quietAnswer.Task;

        var quiet = viewModel.RefreshQuietlyAsync();
        await viewModel.RunAsync(() => Task.CompletedTask);
        quietAnswer.SetException(new DockerUnavailableException("down"));
        await quiet;

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_IgnoresCancellation()
    {
        var viewModel = CreateListViewModel();
        viewModel.OnRefresh = () => throw new OperationCanceledException();

        await viewModel.RefreshQuietlyAsync();

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RefreshQuietly_KeepsTheStatusMessage()
    {
        var viewModel = CreateListViewModel();
        viewModel.SetStatus("Removed 3 containers");

        await viewModel.RefreshQuietlyAsync();

        Assert.Equal("Removed 3 containers", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ManualRefresh_IsBusyAndItsFailureIsClearedByALaterQuietRefresh()
    {
        var viewModel = CreateListViewModel();
        var gate = new TaskCompletionSource();
        viewModel.OnRefresh = () => gate.Task;

        var manual = viewModel.ManualRefreshAsync();

        Assert.True(viewModel.IsBusy);
        gate.SetException(new DockerUnavailableException("down"));
        await manual;
        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);

        viewModel.OnRefresh = () => Task.CompletedTask;
        await viewModel.RefreshQuietlyAsync();

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task ManualRefresh_ClearsThePreviousActionError()
    {
        var viewModel = CreateListViewModel();
        await viewModel.RunAsync(() => throw new InvalidOperationException("action failed"));

        await viewModel.ManualRefreshAsync();

        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void StartAndStopAutoRefresh_DoNothingForAViewModelWithoutSettings()
    {
        var viewModel = new TestViewModel();

        viewModel.StartAutoRefresh();
        viewModel.StopAutoRefresh();

        Assert.Equal(0, viewModel.RefreshCalls);
    }

    [Fact]
    public Task AutoRefresh_RefreshesQuietlyAtTheIntervalOfTheSettings() => UiThread.RunAsync(async ui =>
    {
        var time = new FakeTimeProvider();
        var viewModel = CreateListViewModel(time, refreshSeconds: 10);
        var refreshed = new TaskCompletionSource();
        var busyChanges = 0;
        var refreshThread = 0;
        viewModel.PropertyChanged += (_, e) => busyChanges += e.PropertyName == nameof(PageViewModelBase.IsBusy) ? 1 : 0;
        viewModel.OnRefresh = () =>
        {
            refreshThread = Environment.CurrentManagedThreadId;
            refreshed.TrySetResult();
            return Task.CompletedTask;
        };

        viewModel.StartAutoRefresh();
        time.Advance(TimeSpan.FromSeconds(10));
        await refreshed.Task.Within();
        viewModel.StopAutoRefresh();

        Assert.Equal(1, viewModel.RefreshCalls);
        Assert.Equal(ui.ThreadId, refreshThread);
        Assert.Equal(0, busyChanges);
    });

    [Fact]
    public Task StopAutoRefresh_EndsTheBackgroundRefresh() => UiThread.RunAsync(async () =>
    {
        var time = new FakeTimeProvider();
        var viewModel = CreateListViewModel(time);
        var refreshed = new TaskCompletionSource();
        viewModel.OnRefresh = () =>
        {
            refreshed.TrySetResult();
            return Task.CompletedTask;
        };
        viewModel.StartAutoRefresh();
        time.Advance(TimeSpan.FromSeconds(5));
        await refreshed.Task.Within();

        viewModel.StopAutoRefresh();
        time.Advance(TimeSpan.FromHours(1));
        await Task.Yield();
        await Task.Yield();

        Assert.Equal(1, viewModel.RefreshCalls);
    });
}
