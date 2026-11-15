using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Traysky.Models;

namespace Traysky.Services;

internal static class ThemeService
{
    public static void Apply(FrameworkElement root, AppWindow? window = null)
    {
        ElementTheme requestedTheme = SettingsService.ThemePreference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        root.RequestedTheme = requestedTheme;

        if (window is not null)
        {
            ElementTheme effectiveTheme = requestedTheme == ElementTheme.Default
                ? root.ActualTheme
                : requestedTheme;
            ApplyTitleBar(window, effectiveTheme);
        }
    }

    public static void ApplyTitleBar(AppWindow window, ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        bool light = theme == ElementTheme.Light;
        Color foreground = light ? Colors.Black : Colors.White;
        Color inactiveForeground = Color.FromArgb(0x80, foreground.R, foreground.G, foreground.B);
        Color hoverBackground = Color.FromArgb(light ? (byte)0x0F : (byte)0x20, foreground.R, foreground.G, foreground.B);
        Color pressedBackground = Color.FromArgb(light ? (byte)0x1A : (byte)0x33, foreground.R, foreground.G, foreground.B);

        AppWindowTitleBar titleBar = window.TitleBar;
        titleBar.PreferredTheme = light ? TitleBarTheme.Light : TitleBarTheme.Dark;
        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
