using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WinDocker.Core.Models;

namespace WinDocker.Core.Tests.Support;

/// <summary>
/// A log stream that a test feeds by hand. <see cref="WaitUntilConsumedAsync"/> completes once the reader has
/// finished processing that many lines, which is when the view model's producer has queued them.
/// </summary>
internal sealed class FakeLogStream
{
    private readonly Channel<LogLine> channel = Channel.CreateUnbounded<LogLine>();
    private readonly TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<(string Id, int Tail, bool Follow)> requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(int Count, TaskCompletionSource Signal)> waiters = [];
    private int consumed;

    /// <summary>Completes with the arguments once the view model has asked the service for this stream.</summary>
    public Task<(string Id, int Tail, bool Follow)> Requested => requested.Task;

    /// <summary>Completes when the reader's cancellation token was cancelled.</summary>
    public Task Cancelled => cancelled.Task;

    public void MarkRequested(string id, int tail, bool follow) => requested.TrySetResult((id, tail, follow));

    public void Push(LogLine line) => channel.Writer.TryWrite(line);

    public void Push(string text, bool isError = false) => Push(new LogLine(null, text, isError));

    /// <summary>Ends the stream, like a container that stopped; with an error the reader throws it.</summary>
    public void Complete(Exception? error = null) => channel.Writer.TryComplete(error);

    public Task WaitUntilConsumedAsync(int count)
    {
        lock (waiters)
        {
            if (consumed >= count)
            {
                return Task.CompletedTask;
            }

            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((count, signal));
            return signal.Task;
        }
    }

    public async IAsyncEnumerable<LogLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Cancel() runs its callbacks newest first, so the channel's wait registration fires before this one and the
        // iterator can end and dispose its own registration before Cancel() gets here. Hence the registration is never
        // disposed, and the iterator signals on its way out as well.
        cancellationToken.Register(() => cancelled.TrySetResult());
        try
        {
            await foreach (var line in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return line;

                // Resumed after the consumer asked for the next line, so it is done with this one.
                lock (waiters)
                {
                    consumed++;
                    foreach (var waiter in waiters.Where(waiter => consumed >= waiter.Count).ToList())
                    {
                        waiters.Remove(waiter);
                        waiter.Signal.TrySetResult();
                    }
                }
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
            }
        }
    }
}
