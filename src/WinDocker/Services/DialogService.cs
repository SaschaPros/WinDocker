using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinDocker.Core.Services;

namespace WinDocker.Services;

/// <summary>Confirmation dialogs shown on the main window. Cancel is the default button.</summary>
internal sealed class DialogService(ILocalizer localizer) : IDialogService
{
    public async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        if (App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = message,
            PrimaryButtonText = localizer.GetString(AppResourceKeys.DialogDeleteButton),
            CloseButtonText = localizer.GetString(AppResourceKeys.DialogCancelButton),
            DefaultButton = ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
