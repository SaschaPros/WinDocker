using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Support;

/// <summary>
/// Records every confirmation request and answers it: with the next scripted result, else with <see cref="Answer"/>.
/// A <see cref="Handler"/> takes over completely, for example to keep the dialog open.
/// </summary>
internal sealed class FakeDialogService : IDialogService
{
    private readonly Queue<ConfirmResult> scripted = [];

    /// <summary>The answer when nothing is scripted: confirmed, option unchecked.</summary>
    public ConfirmResult Answer { get; set; } = new(true, false);

    public Func<ConfirmRequest, Task<ConfirmResult>>? Handler { get; set; }

    public List<ConfirmRequest> Requests { get; } = [];

    public void Decline() => Answer = new ConfirmResult(false, false);

    /// <summary>Queues results for the next requests, in order.</summary>
    public void Script(params ConfirmResult[] results)
    {
        foreach (var result in results)
        {
            scripted.Enqueue(result);
        }
    }

    public Task<ConfirmResult> ConfirmAsync(ConfirmRequest request)
    {
        Requests.Add(request);
        if (Handler is not null)
        {
            return Handler(request);
        }

        return Task.FromResult(scripted.Count > 0 ? scripted.Dequeue() : Answer);
    }
}
