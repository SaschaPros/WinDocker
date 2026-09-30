using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinDocker.Core.Services;

namespace WinDocker.Services;

/// <summary>Confirmation dialogs shown on the main window. Cancel is the default button.</summary>
internal sealed class DialogService(ILocalizer localizer) : IDialogService
{
    private bool isOpen;

    public async Task<ConfirmResult> ConfirmAsync(ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Only one dialog can be open at a time, and the Delete key accelerator of a page still works while one is showing.
        if (isOpen || App.MainWindow?.Content?.XamlRoot is not { } xamlRoot)
        {
            return new ConfirmResult(Confirmed: false, OptionChecked: false);
        }

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = false,
            Message = request.Message,
        });

        CheckBox? option = null;
        if (request.OptionText is { } optionText)
        {
            option = new CheckBox { Content = optionText };
            content.Children.Add(option);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = request.Title,
            Content = content,
            PrimaryButtonText = request.PrimaryButtonText,
            CloseButtonText = localizer.GetString(AppResourceKeys.DialogCancelButton),
            DefaultButton = ContentDialogButton.Close,
        };

        isOpen = true;
        try
        {
            var result = await dialog.ShowAsync();
            return new ConfirmResult(result == ContentDialogResult.Primary, option?.IsChecked == true);
        }
        finally
        {
            isOpen = false;
        }
    }
}
