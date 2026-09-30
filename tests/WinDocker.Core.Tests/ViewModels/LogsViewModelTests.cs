using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using Docker.DotNet;
using Microsoft.Extensions.Time.Testing;
using WinDocker.Core.Localization;
using WinDocker.Core.Services;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

public class LogsViewModelTests
{
    private readonly FakeDockerService docker = new();
    private readonly FakeTimeProvider time = new();
    private readonly ConcurrentQueue<FakeLogStream> streams = [];

    public LogsViewModelTests()
    {
        docker.StreamLogsHandler = (id, tail, follow, cancellationToken) =>
        {
            Assert.True(streams.TryDequeue(out var stream), "No stream was queued for this request.");
            stream.MarkRequested(id, tail, follow);
            return stream.ReadAsync(cancellationToken);
        };
    }

    private FakeLogStream QueueStream()
    {
        var stream = new FakeLogStream();
        streams.Enqueue(stream);
        return stream;
    }

    private LogsViewModel CreateViewModel()
    {
        var viewModel = new LogsViewModel(docker, new FakeLocalizer(), time);
        viewModel.Initialize("c1", "web");
        return viewModel;
    }

    private static void Push(FakeLogStream stream, params string[] texts)
    {
        foreach (var text in texts)
        {
            stream.Push(text);
        }
    }

    private static string[] VisibleTexts(LogsViewModel viewModel) => viewModel.VisibleLines.Select(line => line.Text).ToArray();

    [Fact]
    public void Initialize_OnlySetsStateAndStartsNothing()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("c1", viewModel.ContainerId);
        Assert.Equal("web", viewModel.ContainerName);
        Assert.Equal([100, 500, 1000, 5000, 10000], viewModel.TailOptions);
        Assert.Equal(1000, viewModel.Tail);
        Assert.False(viewModel.IsFollowing);
        Assert.Empty(viewModel.VisibleLines);
        Assert.Equal($"{ResourceKeys.LogsLineCount}|0", viewModel.MatchSummary);
        Assert.Empty(docker.StreamCalls);
    }

    [Fact]
    public void Changes_BeforeInitializeDoNotStartStreams()
    {
        var viewModel = new LogsViewModel(docker, new FakeLocalizer(), time);

        viewModel.Tail = 500;
        viewModel.IsFollowing = true;
        viewModel.RefreshCommand.Execute(null);

        Assert.Empty(docker.StreamCalls);
    }

    [Fact]
    public Task Refresh_LoadsTheSnapshot() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        stream.Push("one");
        stream.Push("two", isError: true);
        stream.Push("three");
        stream.Complete();
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["one", "two", "three"], VisibleTexts(viewModel));
        Assert.True(viewModel.VisibleLines[1].IsError);
        Assert.Equal(("c1", 1000, false), await stream.Requested.Within());
        Assert.Equal($"{ResourceKeys.LogsLineCount}|3", viewModel.MatchSummary);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsFollowing);
        Assert.False(viewModel.HasError);
    });

    [Fact]
    public Task Refresh_IsBusyUntilTheSnapshotIsLoaded() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();

        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBusy);
        stream.Push("one");
        stream.Complete();
        await refresh.Within();
        Assert.False(viewModel.IsBusy);
        Assert.Equal(["one"], VisibleTexts(viewModel));
    });

    [Fact]
    public Task Search_FiltersCaseInsensitivelyAfterTheDebounce() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        Push(stream, "Error: boom", "info: ok", "another ERROR", "warning");
        stream.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var before = viewModel.VisibleLines;
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.SearchText = "error";
        time.Advance(LogsViewModel.SearchDebounce - TimeSpan.FromMilliseconds(1));
        Assert.Equal(4, viewModel.VisibleLines.Count);
        Assert.Equal($"{ResourceKeys.LogsLineCount}|4", viewModel.MatchSummary);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await viewModel.SearchCompletion.Within();

        Assert.Equal(["Error: boom", "another ERROR"], VisibleTexts(viewModel));
        Assert.NotSame(before, viewModel.VisibleLines);
        Assert.Equal($"{ResourceKeys.LogsMatchSummary}|2|4", viewModel.MatchSummary);
        Assert.Contains(nameof(LogsViewModel.VisibleLines), raised);
        Assert.Contains(nameof(LogsViewModel.MatchSummary), raised);
    });

    [Fact]
    public Task Search_RestartsTheDebounceOnEveryChange() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        Push(stream, "alpha", "beta", "gamma");
        stream.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "a";
        time.Advance(TimeSpan.FromMilliseconds(200));
        viewModel.SearchText = "gam";
        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(3, viewModel.VisibleLines.Count);

        time.Advance(TimeSpan.FromMilliseconds(50));
        await viewModel.SearchCompletion.Within();

        Assert.Equal(["gamma"], VisibleTexts(viewModel));
    });

    [Fact]
    public Task Search_ClearingItShowsAllLinesAgain() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        Push(stream, "alpha", "beta");
        stream.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SearchText = "alp";
        time.Advance(LogsViewModel.SearchDebounce);
        await viewModel.SearchCompletion.Within();
        Assert.Single(viewModel.VisibleLines);

        viewModel.SearchText = string.Empty;
        time.Advance(LogsViewModel.SearchDebounce);
        await viewModel.SearchCompletion.Within();

        Assert.Equal(["alpha", "beta"], VisibleTexts(viewModel));
        Assert.Equal($"{ResourceKeys.LogsLineCount}|2", viewModel.MatchSummary);
    });

    [Fact]
    public Task Search_MatchesTheTextAndNotTheTimestamp() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        stream.Push(new WinDocker.Core.Models.LogLine(new DateTimeOffset(2026, 9, 29, 10, 15, 30, TimeSpan.Zero), "hello", false));
        stream.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "2026";
        time.Advance(LogsViewModel.SearchDebounce);
        await viewModel.SearchCompletion.Within();

        Assert.Empty(viewModel.VisibleLines);
        Assert.Equal($"{ResourceKeys.LogsMatchSummary}|0|1", viewModel.MatchSummary);
    });

    [Fact]
    public Task Follow_StartsAFollowStreamWithTheTail() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = new LogsViewModel(docker, new FakeLocalizer(), time) { Tail = 500 };
        viewModel.Initialize("c1", "web");
        Assert.Empty(docker.StreamCalls);

        stream.Complete();
        viewModel.IsFollowing = true;

        Assert.Equal(("c1", 500, true), await stream.Requested.Within());
        Assert.False(viewModel.IsBusy);
    });

    [Fact]
    public Task Follow_AppendedLinesRespectTheSearch() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.SearchText = "error";
        time.Advance(LogsViewModel.SearchDebounce);
        await viewModel.SearchCompletion.Within();
        var appended = 0;
        viewModel.LinesAppended += (_, _) => appended++;

        viewModel.IsFollowing = true;
        Push(stream, "info: started", "error: boom", "ERROR again");
        await stream.WaitUntilConsumedAsync(3).Within();
        viewModel.FlushPending();

        Assert.Equal(["error: boom", "ERROR again"], VisibleTexts(viewModel));
        Assert.Equal($"{ResourceKeys.LogsMatchSummary}|2|3", viewModel.MatchSummary);
        Assert.Equal(1, appended);

        viewModel.Stop();
    });

    [Fact]
    public Task Follow_DoesNotRaiseLinesAppendedWhenNoNewLineIsVisible() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.SearchText = "error";
        time.Advance(LogsViewModel.SearchDebounce);
        await viewModel.SearchCompletion.Within();
        var appended = 0;
        viewModel.LinesAppended += (_, _) => appended++;

        viewModel.IsFollowing = true;
        Push(stream, "info: one", "info: two");
        await stream.WaitUntilConsumedAsync(2).Within();
        viewModel.FlushPending();

        Assert.Empty(viewModel.VisibleLines);
        Assert.Equal($"{ResourceKeys.LogsMatchSummary}|0|2", viewModel.MatchSummary);
        Assert.Equal(0, appended);

        viewModel.Stop();
    });

    [Fact]
    public Task Follow_AddsSmallBatchesLineByLine() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        var lines = viewModel.VisibleLines;
        var changes = new List<NotifyCollectionChangedAction>();
        lines.CollectionChanged += (_, e) => changes.Add(e.Action);

        Push(stream, "one", "two", "three");
        await stream.WaitUntilConsumedAsync(3).Within();
        viewModel.FlushPending();

        Assert.Same(lines, viewModel.VisibleLines);
        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add], changes);
        Assert.Equal(["one", "two", "three"], VisibleTexts(viewModel));

        viewModel.Stop();
    });

    [Fact]
    public Task Follow_ReplacesTheCollectionForLargeBatches() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        var lines = viewModel.VisibleLines;
        var appended = 0;
        viewModel.LinesAppended += (_, _) => appended++;

        for (var i = 0; i < LogsViewModel.RebuildThreshold + 1; i++)
        {
            stream.Push($"line {i}");
        }

        await stream.WaitUntilConsumedAsync(LogsViewModel.RebuildThreshold + 1).Within();
        viewModel.FlushPending();

        Assert.NotSame(lines, viewModel.VisibleLines);
        Assert.Equal(LogsViewModel.RebuildThreshold + 1, viewModel.VisibleLines.Count);
        Assert.Equal(1, appended);

        viewModel.Stop();
    });

    [Fact]
    public Task Follow_KeepsAtMostTwentyThousandLines() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;

        for (var i = 0; i < 20_500; i++)
        {
            stream.Push($"line {i}");
        }

        await stream.WaitUntilConsumedAsync(20_500).Within();
        var before = viewModel.VisibleLines;
        viewModel.FlushPending();

        Assert.NotSame(before, viewModel.VisibleLines);
        Assert.Equal(20_000, viewModel.VisibleLines.Count);
        Assert.Equal("line 500", viewModel.VisibleLines[0].Text);
        Assert.Equal("line 20499", viewModel.VisibleLines[^1].Text);
        Assert.Equal($"{ResourceKeys.LogsLineCount}|20000", viewModel.MatchSummary);

        Push(stream, "line 20500", "line 20501");
        await stream.WaitUntilConsumedAsync(20_502).Within();
        viewModel.FlushPending();

        Assert.Equal(20_000, viewModel.VisibleLines.Count);
        Assert.Equal("line 502", viewModel.VisibleLines[0].Text);
        Assert.Equal("line 20501", viewModel.VisibleLines[^1].Text);

        viewModel.Stop();
    });

    [Fact]
    public Task Follow_FlushesOnTheTimerOnTheCallersContext() => UiThread.RunAsync(async ui =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        var appended = new TaskCompletionSource<int>();
        viewModel.LinesAppended += (_, _) => appended.TrySetResult(Environment.CurrentManagedThreadId);
        viewModel.IsFollowing = true;

        Push(stream, "one");
        await stream.WaitUntilConsumedAsync(1).Within();
        Assert.Empty(viewModel.VisibleLines);

        time.Advance(LogsViewModel.FlushInterval);

        Assert.Equal(ui.ThreadId, await appended.Task.Within());
        Assert.Equal(["one"], VisibleTexts(viewModel));

        viewModel.Stop();
    });

    [Fact]
    public Task Follow_TurnsOffWhenTheStreamEnds() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        var completion = viewModel.SessionCompletion;

        Push(stream, "one", "two");
        stream.Complete();
        await completion.Within();

        Assert.False(viewModel.IsFollowing);
        Assert.Equal(["one", "two"], VisibleTexts(viewModel));
        Assert.False(viewModel.HasError);
    });

    [Fact]
    public Task Follow_TurnedOffStopsTheStreamAndKeepsTheLines() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        Push(stream, "one", "two");
        await stream.WaitUntilConsumedAsync(2).Within();

        viewModel.IsFollowing = false;

        await stream.Cancelled.Within();
        Assert.Equal(["one", "two"], VisibleTexts(viewModel));
        Assert.False(viewModel.HasError);
    });

    [Fact]
    public Task Follow_TurnedOnReloadsFromScratch() => UiThread.RunAsync(async () =>
    {
        var snapshot = QueueStream();
        Push(snapshot, "old one", "old two");
        snapshot.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, viewModel.VisibleLines.Count);
        var following = QueueStream();

        viewModel.IsFollowing = true;

        Assert.Empty(viewModel.VisibleLines);
        Assert.Equal(("c1", 1000, true), await following.Requested.Within());
        Push(following, "new one");
        await following.WaitUntilConsumedAsync(1).Within();
        viewModel.FlushPending();
        Assert.Equal(["new one"], VisibleTexts(viewModel));

        viewModel.Stop();
    });

    [Fact]
    public Task Refresh_WhileFollowingRestartsTheFollowStream() => UiThread.RunAsync(async () =>
    {
        var first = QueueStream();
        var second = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        Push(first, "before");
        await first.WaitUntilConsumedAsync(1).Within();
        viewModel.FlushPending();
        Assert.Equal(["before"], VisibleTexts(viewModel));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        await first.Cancelled.Within();
        Assert.Equal(("c1", 1000, true), await second.Requested.Within());
        Assert.Empty(viewModel.VisibleLines);
        Assert.True(viewModel.IsFollowing);
        Push(second, "after");
        await second.WaitUntilConsumedAsync(1).Within();
        viewModel.FlushPending();
        Assert.Equal(["after"], VisibleTexts(viewModel));

        viewModel.Stop();
    });

    [Fact]
    public Task Tail_ChangeReloadsTheLogs() => UiThread.RunAsync(async () =>
    {
        var first = QueueStream();
        Push(first, "a1", "a2");
        first.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var second = QueueStream();

        viewModel.Tail = 500;
        var completion = viewModel.SessionCompletion;
        Push(second, "b1");
        second.Complete();
        await completion.Within();

        Assert.Equal(("c1", 500, false), await second.Requested.Within());
        Assert.Equal(["b1"], VisibleTexts(viewModel));
    });

    [Fact]
    public Task Stop_CancelsTheProducer() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        await stream.Requested.Within();

        viewModel.Stop();

        await stream.Cancelled.Within();
        Assert.False(viewModel.IsFollowing);
    });

    [Fact]
    public Task Stop_CancelsALoadingSnapshotAndItsCommandCompletes() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        var refresh = viewModel.RefreshCommand.ExecuteAsync(null);
        await stream.Requested.Within();

        viewModel.Stop();

        await stream.Cancelled.Within();
        await refresh.Within();
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasError);
    });

    [Fact]
    public Task Stop_CancelsAPendingSearchUpdate() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        Push(stream, "alpha", "beta");
        stream.Complete();
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SearchText = "alpha";

        viewModel.Stop();
        time.Advance(LogsViewModel.SearchDebounce);
        await viewModel.SearchCompletion.Within();

        Assert.Equal(2, viewModel.VisibleLines.Count);
    });

    [Fact]
    public Task Refresh_ShowsALocalizedErrorWhenDockerIsUnavailable() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        stream.Push("partial");
        stream.Complete(new DockerUnavailableException("gone"));
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(ResourceKeys.ErrorDockerUnavailable, viewModel.ErrorMessage);
        Assert.Equal(["partial"], VisibleTexts(viewModel));
        Assert.False(viewModel.IsBusy);
    });

    [Fact]
    public Task Follow_ShowsTheDaemonMessageAndTurnsOffWhenTheStreamFails() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        var completion = viewModel.SessionCompletion;

        stream.Complete(new DockerApiException(HttpStatusCode.NotFound, """{"message":"No such container: c1"}"""));
        await completion.Within();

        Assert.Equal("No such container: c1", viewModel.ErrorMessage);
        Assert.False(viewModel.IsFollowing);
    });

    [Fact]
    public Task Refresh_ShowsTheMessageOfAnyOtherError() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        stream.Complete(new InvalidOperationException("broken"));
        var viewModel = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("broken", viewModel.ErrorMessage);
    });

    [Fact]
    public Task Refresh_ClearsThePreviousError() => UiThread.RunAsync(async () =>
    {
        var failing = QueueStream();
        failing.Complete(new InvalidOperationException("broken"));
        var viewModel = CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasError);
        var working = QueueStream();
        working.Complete();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.HasError);
    });

    [Fact]
    public Task Initialize_AgainDiscardsTheEarlierState() => UiThread.RunAsync(async () =>
    {
        var stream = QueueStream();
        var viewModel = CreateViewModel();
        viewModel.IsFollowing = true;
        Push(stream, "one");
        await stream.WaitUntilConsumedAsync(1).Within();
        viewModel.FlushPending();
        Assert.Single(viewModel.VisibleLines);

        viewModel.Initialize("c2", "db");

        await stream.Cancelled.Within();
        Assert.Equal(("c2", "db"), (viewModel.ContainerId, viewModel.ContainerName));
        Assert.Empty(viewModel.VisibleLines);
        Assert.False(viewModel.IsFollowing);
        Assert.Equal($"{ResourceKeys.LogsLineCount}|0", viewModel.MatchSummary);
    });
}
