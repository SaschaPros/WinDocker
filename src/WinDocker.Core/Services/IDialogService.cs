namespace WinDocker.Core.Services;

public interface IDialogService
{
    /// <summary>Asks the user to confirm a deletion. Returns <see langword="true"/> only when confirmed.</summary>
    Task<bool> ConfirmDeleteAsync(string title, string message);
}
