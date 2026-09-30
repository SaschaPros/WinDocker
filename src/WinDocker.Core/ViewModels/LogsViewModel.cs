using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.ViewModels;

/// <summary>
/// Shows the logs of one container. A producer reads the log stream on the thread pool into a queue; a flush
/// loop on the caller's context (the UI thread) moves the queued lines into the visible list every 150 ms.
/// Create it, call <see cref="Initialize"/>, then execute <see cref="RefreshCommand"/>, and call <see cref="Stop"/> when leaving.
/// </summary>
public sealed partial class LogsViewModel : PageViewModelBase
{
    internal const int MaxLines = 20_000;

    /// <summary>A flush larger than this replaces <see cref="VisibleLines"/>, because WinUI cannot handle range additions.</summary>
    internal const int RebuildThreshold = 1_000;

    internal static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);
    internal static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    private static readonly int[] TailChoices = [100, 500, 1000, 5000, 10000];

    private readonly IDockerService docker;
    private readonly TimeProvider timeProvider;
    private readonly List<LogLine> allLines = [];
    private Session? session;
    private CancellationTokenSource? searchDebounce;
    private string appliedSearch = string.Empty;
    private bool isInitialized;

    public LogsViewModel(IDockerService docker, ILocalizer localizer, TimeProvider timeProvider)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.docker = docker;
        this.timeProvider = timeProvider;
    }

    /// <summary>Raised after lines were added to <see cref="VisibleLines"/>, so the view can scroll to the end.</summary>
    public event EventHandler? LinesAppended;

    [ObservableProperty]
    public partial string ContainerId { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ContainerName { get; private set; } = string.Empty;

    /// <summary>The number of most recent lines to load. Changing it reloads the logs.</summary>
    [ObservableProperty]
    public partial int Tail { get; set; } = 1000;

    public IReadOnlyList<int> TailOptions => TailChoices;

    /// <summary>Case-insensitive text filter. The visible lines follow 250 ms after the last change.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// True while new lines are streamed in. Turning it on reloads the logs and follows them, turning it off
    /// stops the stream and keeps the lines. It turns off by itself when the stream ends, for example when the container stops.
    /// </summary>
    [ObservableProperty]
    public partial bool IsFollowing { get; set; }

    /// <summary>The lines matching the search. The instance is replaced on reloads, searches and large updates.</summary>
    [ObservableProperty]
    public partial ObservableCollection<LogLine> VisibleLines { get; private set; } = [];

    public string MatchSummary => string.IsNullOrEmpty(appliedSearch)
        ? Localizer.Format(ResourceKeys.LogsLineCount, allLines.Count)
        : Localizer.Format(ResourceKeys.LogsMatchSummary, VisibleLines.Count, allLines.Count);

    /// <summary>Completion of the current stream (test hook).</summary>
    internal Task SessionCompletion => session?.Completion ?? Task.CompletedTask;

    /// <summary>Completion of the pending search update (test hook).</summary>
    internal Task SearchCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>Sets the container to show and discards earlier state. Execute <see cref="RefreshCommand"/> afterwards to load the logs.</summary>
    public void Initialize(string containerId, string containerName)
    {
        ArgumentException.ThrowIfNullOrEmpty(containerId);

        Stop();
        isInitialized = false;
        ContainerId = containerId;
        ContainerName = containerName;
        ErrorMessage = null;
        allLines.Clear();
        appliedSearch = SearchText;
        RebuildVisibleLines();
        isInitialized = true;
    }

    /// <summary>Cancels the stream and any pending search update. Call when the page is left.</summary>
    public void Stop()
    {
        CancelSearchDebounce();
        CancelSession();
        if (IsFollowing)
        {
            IsFollowing = false;
        }
    }

    partial void OnTailChanged(int value)
    {
        if (isInitialized)
        {
            _ = RefreshAsync();
        }
    }

    partial void OnIsFollowingChanged(bool value)
    {
        if (!isInitialized)
        {
            return;
        }

        if (value)
        {
            StartSession();
        }
        else
        {
            FlushPending();
            CancelSession();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        CancelSearchDebounce();
        var debounce = searchDebounce = new CancellationTokenSource();
        SearchCompletion = ApplySearchAfterDelayAsync(debounce.Token);
    }

    /// <summary>Reloads the last <see cref="Tail"/> lines, and keeps following when <see cref="IsFollowing"/> is on.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!isInitialized)
        {
            return;
        }

        var current = StartSession();
        if (!current.Follow)
        {
            await current.Completion;
        }
    }

    private Session StartSession()
    {
        CancelSession();
        ErrorMessage = null;
        allLines.Clear();
        RebuildVisibleLines();

        var current = new Session(IsFollowing);
        session = current;
        current.Completion = RunSessionAsync(current, ContainerId, Tail);
        return current;
    }

    private void CancelSession()
    {
        session?.Cancel();
        session = null;
    }

    private void CancelSearchDebounce()
    {
        searchDebounce?.Cancel();
        searchDebounce?.Dispose();
        searchDebounce = null;
    }

    private async Task ApplySearchAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SearchDebounce, timeProvider, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        appliedSearch = SearchText;
        RebuildVisibleLines();
    }

    /// <summary>Runs on the caller's context: starts the producer, flushes on a timer, and finishes when the stream ends.</summary>
    private async Task RunSessionAsync(Session current, string containerId, int tail)
    {
        var token = current.Token;
        if (!current.Follow)
        {
            BeginBusy();
        }

        try
        {
            using var producerDone = CancellationTokenSource.CreateLinkedTokenSource(token);
            var producer = Task.Run(
                async () =>
                {
                    try
                    {
                        await foreach (var line in docker.StreamLogsAsync(containerId, tail, current.Follow, token))
                        {
                            current.Pending.Enqueue(line);
                        }
                    }
                    finally
                    {
                        producerDone.Cancel();
                    }
                },
                CancellationToken.None);

            try
            {
                using var timer = new PeriodicTimer(FlushInterval, timeProvider);
                while (await timer.WaitForNextTickAsync(producerDone.Token))
                {
                    FlushPending(current);
                }
            }
            catch (OperationCanceledException)
            {
                // The producer finished or the session was cancelled.
            }

            Exception? failure = null;
            try
            {
                await producer;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (token.IsCancellationRequested)
            {
                // Replaced by a newer stream, or stopped: leave the state to whoever cancelled.
                return;
            }

            FlushPending(current);
            if (failure is not null)
            {
                ErrorMessage = DescribeError(failure);
            }

            if (current.Follow)
            {
                IsFollowing = false;
            }
        }
        finally
        {
            if (!current.Follow)
            {
                EndBusy();
            }

            // Also stops a producer that is still running when this method fails unexpectedly.
            current.Cancel();
            current.Dispose();
        }
    }

    /// <summary>Moves the queued lines of the current stream into the visible list (also called by the timer).</summary>
    internal void FlushPending()
    {
        if (session is { } current)
        {
            FlushPending(current);
        }
    }

    private void FlushPending(Session current)
    {
        if (!ReferenceEquals(current, session))
        {
            return;
        }

        var count = current.Pending.Count;
        if (count == 0)
        {
            return;
        }

        var batch = new List<LogLine>(count);
        while (batch.Count < count && current.Pending.TryDequeue(out var line))
        {
            batch.Add(line);
        }

        allLines.AddRange(batch);
        var trimmed = allLines.Count > MaxLines;
        if (trimmed)
        {
            allLines.RemoveRange(0, allLines.Count - MaxLines);
        }

        if (trimmed || batch.Count > RebuildThreshold)
        {
            RebuildVisibleLines();
        }
        else
        {
            var appended = false;
            foreach (var line in batch.Where(Matches))
            {
                VisibleLines.Add(line);
                appended = true;
            }

            OnPropertyChanged(nameof(MatchSummary));
            if (!appended)
            {
                return;
            }
        }

        LinesAppended?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildVisibleLines()
    {
        VisibleLines = new ObservableCollection<LogLine>(string.IsNullOrEmpty(appliedSearch) ? allLines : allLines.Where(Matches));
        OnPropertyChanged(nameof(MatchSummary));
    }

    private bool Matches(LogLine line) =>
        string.IsNullOrEmpty(appliedSearch) || line.Text.Contains(appliedSearch, StringComparison.OrdinalIgnoreCase);

    /// <summary>One stream of log lines: a producer fills <see cref="Pending"/>, the flush loop drains it.</summary>
    private sealed class Session : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();

        public Session(bool follow)
        {
            Follow = follow;
            Token = cancellation.Token;
        }

        public bool Follow { get; }

        public CancellationToken Token { get; }

        public ConcurrentQueue<LogLine> Pending { get; } = new();

        public Task Completion { get; set; } = Task.CompletedTask;

        public void Cancel()
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The stream already ended.
            }
        }

        public void Dispose() => cancellation.Dispose();
    }
}
