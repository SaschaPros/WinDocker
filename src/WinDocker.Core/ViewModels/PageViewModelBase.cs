using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using Docker.DotNet;
using WinDocker.Core.Docker;
using WinDocker.Core.Localization;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public abstract partial class PageViewModelBase : ObservableObject
{
    private const int MaxBulkParallelism = 4;
    private const int MaxReportedFailures = 3;

    private readonly AutoRefreshLoop? autoRefresh;
    private int busyCount;
    private int confirmationCount;
    private int operationVersion;
    private string? errorMessage;
    private ErrorOrigin errorOrigin;
    private string? dismissedRefreshError;

    protected PageViewModelBase(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        Localizer = localizer;
    }

    /// <summary>For list view models: the list refreshes itself in the background while <see cref="StartAutoRefresh"/> is active.</summary>
    protected PageViewModelBase(ILocalizer localizer, SettingsService settings, TimeProvider timeProvider)
        : this(localizer)
    {
        autoRefresh = new AutoRefreshLoop(settings, timeProvider);
    }

    /// <summary>Where an error came from, which decides who may replace or clear it.</summary>
    private enum ErrorOrigin
    {
        /// <summary>A user action (start, stop, remove, ...).</summary>
        Action,

        /// <summary>Loading the list, either by the refresh command or in the background.</summary>
        Refresh,
    }

    protected ILocalizer Localizer { get; }

    /// <summary>True while at least one operation is running. Background refreshes do not count.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>The error to show. An error set through this property counts as the error of a user action.</summary>
    public string? ErrorMessage
    {
        get => errorMessage;
        protected set => SetError(value, ErrorOrigin.Action);
    }

    /// <summary>
    /// True while <see cref="ErrorMessage"/> is set. Setting it to <see langword="false"/> dismisses the error,
    /// so it can be bound two-way to <c>InfoBar.IsOpen</c>. A dismissed refresh error does not come back with the
    /// next failed background refresh, only once a refresh succeeded in between or the failure is a different one.
    /// </summary>
    public bool HasError
    {
        get => ErrorMessage is not null;
        set
        {
            if (value)
            {
                return;
            }

            if (errorMessage is not null && errorOrigin == ErrorOrigin.Refresh)
            {
                dismissedRefreshError = errorMessage;
            }

            ErrorMessage = null;
        }
    }

    /// <summary>A success message for the last action, for example what a prune removed. Cleared when the next action starts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusMessage { get; protected set; }

    /// <summary>True while <see cref="StatusMessage"/> is set. Setting it to <see langword="false"/> dismisses the message.</summary>
    public bool HasStatus
    {
        get => StatusMessage is not null;
        set
        {
            if (!value)
            {
                StatusMessage = null;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(HasSingleSelection))]
    public partial int SelectionCount { get; private set; }

    public bool HasSelection => SelectionCount > 0;

    public bool HasSingleSelection => SelectionCount == 1;

    /// <summary>True while a confirmation dialog opened through <see cref="ConfirmAsync"/> is showing.</summary>
    protected bool IsConfirming => Volatile.Read(ref confirmationCount) > 0;

    /// <summary>
    /// Starts refreshing the list in the background at the interval of the settings. Call it when the page is shown
    /// and pair it with <see cref="StopAutoRefresh"/>. Does nothing for a view model that was created without settings.
    /// </summary>
    public void StartAutoRefresh() => autoRefresh?.Start(RefreshQuietlyAsync, CanRefreshQuietly);

    public void StopAutoRefresh() => autoRefresh?.Stop();

    /// <summary>
    /// Reloads the list without touching <see cref="IsBusy"/>, so the progress bar stays still. Does nothing while an
    /// action runs or a confirmation is open. A failure is reported unless an action's error is showing; a success
    /// clears the error of an earlier refresh (for example "engine unavailable") but never that of an action.
    /// </summary>
    public async Task RefreshQuietlyAsync()
    {
        if (!CanRefreshQuietly())
        {
            return;
        }

        // An action or a manual refresh that starts meanwhile reloads the list itself and owns the error state.
        var version = operationVersion;
        try
        {
            await RefreshCoreAsync();
            if (version == operationVersion)
            {
                dismissedRefreshError = null;
                if (errorMessage is not null && errorOrigin == ErrorOrigin.Refresh)
                {
                    ErrorMessage = null;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            var message = DescribeError(exception);
            var actionErrorShowing = errorMessage is not null && errorOrigin == ErrorOrigin.Action;
            if (version == operationVersion && !actionErrorShowing && message != dismissedRefreshError)
            {
                SetError(message, ErrorOrigin.Refresh);
            }
        }
    }

    /// <summary>Loads the list. Overridden by the list view models; used by the refresh command and the background refresh.</summary>
    protected virtual Task RefreshCoreAsync() => Task.CompletedTask;

    /// <summary>Sets the number of selected items; the derived classes call it when the view pushes a new selection.</summary>
    protected void SetSelectionCount(int count) => SelectionCount = count;

    /// <summary>
    /// Runs <paramref name="action"/> with <see cref="IsBusy"/> set, after clearing the previous error and status message.
    /// A failure becomes <see cref="ErrorMessage"/> instead of an exception, as the error of a user action.
    /// </summary>
    protected Task RunSafeAsync(Func<Task> action) => RunCoreAsync(action, ErrorOrigin.Action);

    /// <summary>Like <see cref="RunSafeAsync"/>, for a load of the list: a failure is the error of a refresh, which a later refresh may clear.</summary>
    protected Task RunRefreshAsync(Func<Task> refresh) => RunCoreAsync(refresh, ErrorOrigin.Refresh);

    /// <summary>
    /// Runs a change (start, stop, remove) and then reloads the list. When the engine rejected the change,
    /// the list may be out of date, so it is reloaded as well and the engine's message stays visible.
    /// </summary>
    protected Task RunMutationAsync(Func<Task> mutation, Func<Task> reload) =>
        RunSafeAsync(async () =>
        {
            try
            {
                await mutation();
            }
            catch (DockerApiException)
            {
                try
                {
                    await reload();
                }
                catch (Exception)
                {
                    // Keep the original error.
                }

                throw;
            }

            await reload();
        });

    /// <summary>
    /// Runs <paramref name="action"/> for every target, at most four at a time, then reloads once and reports the failures.
    /// The action runs on the thread pool and must not touch view model state, so pass immutable snapshots as targets.
    /// One failure is described like any other error; several as "n of m failed" and the first three as name and message
    /// (<paramref name="displayName"/> gives the name). When the engine is unreachable only that is reported and nothing is reloaded.
    /// </summary>
    protected Task RunBulkAsync<T>(IReadOnlyList<T> targets, Func<T, Task> action, Func<T, string> displayName, Func<Task> reload)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(reload);

        if (targets.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunSafeAsync(async () =>
        {
            var failures = await RunAllAsync(targets, action);
            if (failures.Any(failure => failure.Error is DockerUnavailableException))
            {
                ErrorMessage = Localizer.GetString(ResourceKeys.ErrorDockerUnavailable);
                return;
            }

            try
            {
                await reload();
            }
            catch (Exception) when (failures.Count > 0)
            {
                // Keep the failures of the action.
            }

            if (failures.Count > 0)
            {
                ErrorMessage = DescribeFailures(failures, targets, displayName);
            }
        });
    }

    /// <summary>Shows a confirmation dialog. While it is open, background refreshes are skipped.</summary>
    protected async Task<ConfirmResult> ConfirmAsync(IDialogService dialogs, ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(request);

        Interlocked.Increment(ref confirmationCount);
        try
        {
            return await dialogs.ConfirmAsync(request);
        }
        finally
        {
            Interlocked.Decrement(ref confirmationCount);
        }
    }

    protected string DescribeError(Exception exception) => exception switch
    {
        DockerUnavailableException => Localizer.GetString(ResourceKeys.ErrorDockerUnavailable),
        DockerApiException apiException => DockerFormat.DaemonMessage(apiException.ResponseBody) ?? apiException.Message,
        _ => exception.Message,
    };

    protected void BeginBusy()
    {
        if (Interlocked.Increment(ref busyCount) == 1)
        {
            IsBusy = true;
        }
    }

    protected void EndBusy()
    {
        if (Interlocked.Decrement(ref busyCount) == 0)
        {
            IsBusy = false;
        }
    }

    private static async Task<IReadOnlyList<(int Index, Exception Error)>> RunAllAsync<T>(IReadOnlyList<T> targets, Func<T, Task> action)
    {
        var failures = new ConcurrentBag<(int Index, Exception Error)>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxBulkParallelism, TaskScheduler = TaskScheduler.Default };
        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Count),
            options,
            async (index, _) =>
            {
                try
                {
                    await action(targets[index]);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    failures.Add((index, exception));
                }
            });

        return [.. failures.OrderBy(failure => failure.Index)];
    }

    private bool CanRefreshQuietly() => !IsBusy && !IsConfirming;

    private async Task RunCoreAsync(Func<Task> action, ErrorOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(action);

        BeginBusy();
        operationVersion++;
        dismissedRefreshError = null;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetError(DescribeError(exception), origin);
        }
        finally
        {
            EndBusy();
        }
    }

    private string DescribeFailures<T>(IReadOnlyList<(int Index, Exception Error)> failures, IReadOnlyList<T> targets, Func<T, string> displayName)
    {
        if (failures.Count == 1)
        {
            return DescribeError(failures[0].Error);
        }

        var lines = failures
            .Take(MaxReportedFailures)
            .Select(failure => $"{displayName(targets[failure.Index])}: {DescribeError(failure.Error)}")
            .Prepend(Localizer.Format(ResourceKeys.BulkPartialFailure, failures.Count, targets.Count));
        return string.Join('\n', lines);
    }

    private void SetError(string? message, ErrorOrigin origin)
    {
        errorOrigin = origin;
        if (SetProperty(ref errorMessage, message, nameof(ErrorMessage)))
        {
            OnPropertyChanged(nameof(HasError));
        }
    }
}
