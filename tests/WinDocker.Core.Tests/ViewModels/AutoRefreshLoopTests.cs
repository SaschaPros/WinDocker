using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using WinDocker.Core.Settings;
using WinDocker.Core.Tests.Support;
using WinDocker.Core.ViewModels;

namespace WinDocker.Core.Tests.ViewModels;

/// <summary>
/// Every test runs on a <see cref="UiThread"/>, like the loop does in the app. Waiting is done by yielding to that thread
/// until the loop reached a known state, never by sleeping, and the time is a <see cref="FakeTimeProvider"/>.
/// </summary>
public class AutoRefreshLoopTests
{
    private sealed class Harness
    {
        public Harness(int refreshSeconds)
        {
            Settings = FakeSettingsStore.CreateService(refreshSeconds);
            Loop = new AutoRefreshLoop(Settings, Time);
        }

        public FakeTimeProvider Time { get; } = new();

        public SettingsService Settings { get; }

        public AutoRefreshLoop Loop { get; }

        /// <summary>"can" and "cannot" for every evaluation of canTick, "tick" for every tick.</summary>
        public List<string> Events { get; } = [];

        public bool CanTick { get; set; } = true;

        public Func<Task> TickBody { get; set; } = () => Task.CompletedTask;

        public int Ticks => Events.Count(entry => entry == "tick");

        public void Start() => Loop.Start(Tick, CanTickNow);

        public void Advance(int seconds) => Time.Advance(TimeSpan.FromSeconds(seconds));

        private Task Tick()
        {
            Events.Add("tick");
            return TickBody();
        }

        private bool CanTickNow()
        {
            Events.Add(CanTick ? "can" : "cannot");
            return CanTick;
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TestWait.Timeout)
            {
                throw new TimeoutException("The loop did not reach the expected state.");
            }

            await Task.Yield();
        }
    }

    /// <summary>Lets everything that is already queued on the UI thread run, including short chains of follow-ups.</summary>
    private static async Task Settle()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            await Task.Yield();
        }
    }

    /// <summary>Waits until the loop has begun its <paramref name="waits"/>th wait, which means its timer exists.</summary>
    private static Task Armed(Harness harness, int waits) => Until(() => harness.Loop.Waits >= waits);

    [Fact]
    public Task Ticks_OnceEveryInterval() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        harness.Start();
        await Armed(harness, 1);

        harness.Advance(4);
        await Settle();
        Assert.Equal(0, harness.Ticks);

        harness.Advance(1);
        await Until(() => harness.Ticks == 1);
        await Armed(harness, 2);

        harness.Advance(4);
        await Settle();
        Assert.Equal(1, harness.Ticks);

        harness.Advance(1);
        await Until(() => harness.Ticks == 2);
        Assert.Equal(["can", "tick", "can", "tick"], harness.Events);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task TheFirstTickComesAfterTheIntervalNotAtOnce() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(2);

        harness.Start();
        await Settle();

        Assert.Empty(harness.Events);
        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task Off_NeverTicksButWaitsForASettingChange() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(0);
        harness.Start();
        await Armed(harness, 1);

        harness.Time.Advance(TimeSpan.FromHours(24));
        await Settle();
        Assert.Empty(harness.Events);

        harness.Settings.RefreshInterval = TimeSpan.FromSeconds(5);
        await Armed(harness, 2);
        harness.Advance(4);
        await Settle();
        Assert.Empty(harness.Events);

        harness.Advance(1);
        await Until(() => harness.Ticks == 1);
        Assert.Equal(["can", "tick"], harness.Events);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task IntervalChange_StartsANewWaitAtOnce() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(60);
        harness.Start();
        await Armed(harness, 1);
        harness.Advance(30);
        await Settle();

        harness.Settings.RefreshInterval = TimeSpan.FromSeconds(2);
        await Armed(harness, 2);

        harness.Advance(1);
        await Settle();
        Assert.Empty(harness.Events);

        harness.Advance(1);
        await Until(() => harness.Ticks == 1);
        Assert.Equal(["can", "tick"], harness.Events);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task IntervalChange_ToALongerOneAlsoRestartsTheWait() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        harness.Start();
        await Armed(harness, 1);
        harness.Advance(4);

        harness.Settings.RefreshInterval = TimeSpan.FromSeconds(10);
        await Armed(harness, 2);
        harness.Advance(9);
        await Settle();
        Assert.Empty(harness.Events);

        harness.Advance(1);
        await Until(() => harness.Ticks == 1);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task IntervalChange_ToOffStopsTheTicksWithoutStoppingTheLoop() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        harness.Start();
        await Armed(harness, 1);
        harness.Advance(3);

        harness.Settings.RefreshInterval = TimeSpan.Zero;
        await Armed(harness, 2);
        harness.Time.Advance(TimeSpan.FromHours(1));
        await Settle();

        Assert.Empty(harness.Events);
        Assert.False(harness.Loop.Completion.IsCompleted);

        harness.Settings.RefreshInterval = TimeSpan.FromSeconds(2);
        await Armed(harness, 3);
        harness.Advance(2);
        await Until(() => harness.Ticks == 1);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task CanTickFalse_SkipsTheRoundWithoutTicking() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5) { CanTick = false };
        harness.Start();
        await Armed(harness, 1);

        harness.Advance(5);
        await Until(() => harness.Events.Count == 1);
        await Armed(harness, 2);
        Assert.Equal(["cannot"], harness.Events);

        harness.CanTick = true;
        harness.Advance(5);
        await Until(() => harness.Ticks == 1);

        Assert.Equal(["cannot", "can", "tick"], harness.Events);
        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task Stop_HaltsTheLoop() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        harness.Start();
        await Armed(harness, 1);
        harness.Advance(5);
        await Until(() => harness.Ticks == 1);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
        harness.Time.Advance(TimeSpan.FromHours(1));
        await Settle();

        Assert.Equal(1, harness.Ticks);
    });

    [Fact]
    public Task Stop_EndsTheWaitPromptlyWithoutTheTimeElapsing() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(60);
        harness.Start();
        await Armed(harness, 1);

        harness.Loop.Stop();

        await harness.Loop.Completion.Within();
        Assert.Empty(harness.Events);
    });

    [Fact]
    public Task Stop_EndsTheWaitOfARefreshThatIsOff() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(0);
        harness.Start();
        await Armed(harness, 1);

        harness.Loop.Stop();

        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task Stop_WithoutStartAndTwiceIsHarmless() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        harness.Loop.Stop();
        harness.Start();
        await Armed(harness, 1);

        harness.Loop.Stop();
        harness.Loop.Stop();

        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task Stop_WhileATickRunsLetsItFinishAndDoesNotTickAgain() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        var release = new TaskCompletionSource();
        harness.TickBody = () => release.Task;
        harness.Start();
        await Armed(harness, 1);
        harness.Advance(5);
        await Until(() => harness.Ticks == 1);

        harness.Loop.Stop();
        Assert.False(harness.Loop.Completion.IsCompleted);
        release.SetResult();
        await harness.Loop.Completion.Within();
        harness.Time.Advance(TimeSpan.FromHours(1));
        await Settle();

        Assert.Equal(1, harness.Ticks);
    });

    [Fact]
    public Task Start_WhileRunningRestartsTheLoop() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        var ticksOfTheOldLoop = 0;
        harness.Loop.Start(
            () =>
            {
                ticksOfTheOldLoop++;
                return Task.CompletedTask;
            },
            () => true);
        var oldLoop = harness.Loop.Completion;
        await Armed(harness, 1);
        harness.Advance(4);

        harness.Start();
        await oldLoop.Within();
        await Armed(harness, 2);

        harness.Advance(4);
        await Settle();
        Assert.Empty(harness.Events);

        harness.Advance(1);
        await Until(() => harness.Ticks == 1);

        Assert.Equal(0, ticksOfTheOldLoop);
        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task Ticks_NeverOverlap() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        var release = new TaskCompletionSource();
        harness.TickBody = () => release.Task;
        harness.Start();
        await Armed(harness, 1);

        harness.Advance(5);
        await Until(() => harness.Ticks == 1);
        harness.Advance(5);
        harness.Advance(5);
        await Settle();
        Assert.Equal(1, harness.Ticks);

        harness.TickBody = () => Task.CompletedTask;
        release.SetResult();
        await Armed(harness, 2);
        harness.Advance(5);
        await Until(() => harness.Ticks == 2);

        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task ATickThatThrowsDoesNotEndTheLoop() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(5);
        var calls = 0;
        harness.TickBody = () =>
        {
            calls++;
            return calls switch
            {
                1 => throw new InvalidOperationException("thrown"),
                2 => Task.FromException(new InvalidOperationException("faulted")),
                _ => Task.CompletedTask,
            };
        };
        harness.Start();

        for (var expected = 1; expected <= 3; expected++)
        {
            await Armed(harness, expected);
            harness.Advance(5);
            await Until(() => harness.Ticks == expected);
        }

        Assert.False(harness.Loop.Completion.IsCompleted);
        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task TicksRunOnTheContextThatStartedTheLoop() => UiThread.RunAsync(async ui =>
    {
        var harness = new Harness(5);
        var threads = new List<int>();
        harness.TickBody = () =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            return Task.CompletedTask;
        };
        harness.Start();
        await Armed(harness, 1);

        harness.Advance(5);
        await Until(() => harness.Ticks == 1);
        await Armed(harness, 2);
        harness.Advance(5);
        await Until(() => harness.Ticks == 2);

        Assert.Equal([ui.ThreadId, ui.ThreadId], threads);
        harness.Loop.Stop();
        await harness.Loop.Completion.Within();
    });

    [Fact]
    public Task AnIntervalBeyondWhatTheTimerSupportsDoesNotBreakTheLoop() => UiThread.RunAsync(async () =>
    {
        var harness = new Harness(int.MaxValue);
        harness.Start();
        await Armed(harness, 1);
        Assert.False(harness.Loop.Completion.IsCompleted);

        harness.Loop.Stop();

        await harness.Loop.Completion.Within();
        Assert.Empty(harness.Events);
    });

    [Fact]
    public void Constructor_RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new AutoRefreshLoop(null!, new FakeTimeProvider()));
        Assert.Throws<ArgumentNullException>(() => new AutoRefreshLoop(FakeSettingsStore.CreateService(), null!));
    }

    [Fact]
    public void Start_RejectsMissingArguments()
    {
        var loop = new AutoRefreshLoop(FakeSettingsStore.CreateService(), new FakeTimeProvider());

        Assert.Throws<ArgumentNullException>(() => loop.Start(null!, () => true));
        Assert.Throws<ArgumentNullException>(() => loop.Start(() => Task.CompletedTask, null!));
    }
}
