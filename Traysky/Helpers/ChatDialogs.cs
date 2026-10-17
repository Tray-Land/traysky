using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Traysky.Helpers;

/// <summary>Confirmations for the destructive chat actions.</summary>
public static class ChatDialogs
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string button)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = button,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
