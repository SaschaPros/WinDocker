using System.Collections.Concurrent;

namespace WinDocker.Core.Tests.Support;

/// <summary>
/// A single-threaded <see cref="SynchronizationContext"/> that stands in for the UI thread: the test body and
/// every continuation of the code under test run one at a time on it, like in the WinUI app.
/// </summary>
internal sealed class UiThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];

    private UiThread()
    {
    }

    public int ThreadId { get; private set; }

    public static async Task RunAsync(Func<UiThread, Task> body)
    {
        var context = new UiThread();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(context);
            context.ThreadId = Environment.CurrentManagedThreadId;
            context.Post(
                async _ =>
                {
                    try
                    {
                        await body(context);
                        completion.SetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                    finally
                    {
                        context.queue.CompleteAdding();
                    }
                },
                null);

            foreach (var (callback, state) in context.queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        })
        {
            IsBackground = true,
            Name = "Test UI thread",
        };

        thread.Start();
        await completion.Task;
    }

    public static Task RunAsync(Func<Task> body) => RunAsync(_ => body());

    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            // The test has ended; late continuations are dropped.
        }
    }
}
