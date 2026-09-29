using WinDocker.Core.Services;

namespace WinDocker.Core.Tests.Support;

internal sealed class FakeDialogService : IDialogService
{
    public bool Answer { get; set; } = true;

    public List<(string Title, string Message)> Requests { get; } = [];

    public Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        Requests.Add((title, message));
        return Task.FromResult(Answer);
    }
}
