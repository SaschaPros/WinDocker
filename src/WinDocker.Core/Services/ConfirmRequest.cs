namespace WinDocker.Core.Services;

/// <summary>What a confirmation dialog shows. All texts are already localized.</summary>
/// <param name="Title">Dialog title.</param>
/// <param name="Message">What is going to happen.</param>
/// <param name="PrimaryButtonText">Text of the confirming button; the dialog adds a cancel button of its own.</param>
/// <param name="OptionText">Label of an additional check box, or <see langword="null"/> for a dialog without one.</param>
public sealed record ConfirmRequest(string Title, string Message, string PrimaryButtonText, string? OptionText = null);
