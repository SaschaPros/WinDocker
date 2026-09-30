namespace WinDocker.Core.Services;

public interface IDialogService
{
    /// <summary>Asks the user to confirm an action. Cancelling (or closing the dialog) answers <see cref="ConfirmResult.Confirmed"/> = <see langword="false"/>.</summary>
    Task<ConfirmResult> ConfirmAsync(ConfirmRequest request);
}
