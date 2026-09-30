namespace WinDocker.Core.Services;

/// <param name="Confirmed">True when the user pressed the primary button.</param>
/// <param name="OptionChecked">State of the check box when <see cref="ConfirmRequest.OptionText"/> was set; otherwise false.</param>
public readonly record struct ConfirmResult(bool Confirmed, bool OptionChecked);
