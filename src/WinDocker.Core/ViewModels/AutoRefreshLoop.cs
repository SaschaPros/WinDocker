using System.ComponentModel;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

/// <summary>
/// Calls a tick every <see cref="SettingsService.RefreshInterval"/>. The loop runs on the context that calls
/// <see cref="Start"/> (the UI thread), so the tick can touch view model state. Each round waits for the interval
/// or for the setting to change, whichever comes first, and then starts over with the current setting; while
/// refreshing is off it only waits for a change.
/// </summary>
public sealed class AutoRefreshLoop
{
    /// <summary>The longest wait the timer supports.</summary>
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly SettingsService settings;
    private readonly TimeProvider timeProvider;
    private CancellationTokenSource? running;

    public AutoRefreshLoop(SettingsService settings, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settings = settings;
        this.timeProvider = timeProvider;
    }

    /// <summary>Completion of the current loop (test hook).</summary>
    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>How many waits have begun so far, over all runs; a test knows the loop is armed once it grows (test hook).</summary>
    internal int Waits { get; private set; }

    /// <summary>
    /// Starts the loop, or restarts it when it is already running. After each wait, <paramref name="tick"/> is
    /// called if <paramref name="canTick"/> allows it; otherwise the round is skipped.
    /// </summary>
    public void Start(Func<Task> tick, Func<bool> canTick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(canTick);

        Stop();
        var source = running = new CancellationTokenSource();
        Completion = RunAsync(tick, canTick, source);
    }

    /// <summary>Ends the loop without waiting for the current interval. A tick that is already running finishes first.</summary>
    public void Stop()
    {
        var source = running;
        running = null;
        Cancel(source);
    }

    private static void Cancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The loop already ended.
        }
    }

    private async Task RunAsync(Func<Task> tick, Func<bool> canTick, CancellationTokenSource source)
    {
        var token = source.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!await WaitAsync(token) || token.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    if (canTick())
                    {
                        await tick();
                    }
                }
                catch (Exception)
                {
                    // The tick reports its own errors; whatever it throws must not end the loop.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <returns>True when the interval elapsed, false when the setting changed first. Throws when <paramref name="stopToken"/> is cancelled.</returns>
    private async Task<bool> WaitAsync(CancellationToken stopToken)
    {
        using var round = CancellationTokenSource.CreateLinkedTokenSource(stopToken);

        void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsService.RefreshInterval))
            {
                Cancel(round);
            }
        }

        // Subscribing before reading the interval means a change cannot slip through in between.
        settings.PropertyChanged += OnSettingsChanged;
        try
        {
            var interval = settings.RefreshInterval;
            var delay = interval == TimeSpan.Zero ? Timeout.InfiniteTimeSpan : interval > MaxDelay ? MaxDelay : interval;
            Waits++;
            await Task.Delay(delay, timeProvider, round.Token);
            return true;
        }
        catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            settings.PropertyChanged -= OnSettingsChanged;
        }
    }
}
