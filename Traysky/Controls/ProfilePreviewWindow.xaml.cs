using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using Traysky.Pages;
using Traysky.Services;
using Traysky.ViewModels;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;
using WinUIEx;

namespace Traysky.Controls;

/// <summary>
/// A look at a profile, opened from the compose box's mention list so the user can check who
/// they're about to tag. It hosts <see cref="ProfilePage"/> in contained mode in a frame of its
/// own, so nothing in it can navigate the tray popup or disturb the draft. It is reused for the
/// next review, and goes away when the post is sent or the app closes.
/// </summary>
public sealed partial class ProfilePreviewWindow : WindowEx
{
    /// <summary>Clear space between this window and the popup, in DIPs.</summary>
    private const int Gap = 12;

    private static ProfilePreviewWindow? s_current;

    private ProfilePreviewWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(App.IsAppInDarkMode() ? "Assets\\Wings-Light.ico" : "Assets\\Wings-Dark.ico");
        IsAlwaysOnTop = true;
        IsMaximizable = false;
        IsMinimizable = true;
        IsResizable = true;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        ContentFrame.Navigated += (_, _) =>
            BackButton.Visibility = ContentFrame.CanGoBack ? Visibility.Visible : Visibility.Collapsed;

        ComposeViewModel.Instance.Posted += OnPosted;
        Closed += (_, _) =>
        {
            ComposeViewModel.Instance.Posted -= OnPosted;
            s_current = null;
        };
    }

    /// <summary>Shows <paramref name="didOrHandle"/>'s profile, opening the window if needed.</summary>
    public static void Show(string didOrHandle)
    {
        if (string.IsNullOrWhiteSpace(didOrHandle))
            return;

        bool isNew = s_current is null;
        s_current ??= new ProfilePreviewWindow();
        s_current.ShowProfile(didOrHandle.Trim().TrimStart('@'));

        if (isNew)
        {
            s_current.SizeAndPlace();

            // Shown without activation so the popup keeps focus and the draft stays where it was.
            s_current.AppWindow.Show(activateWindow: false);
        }
    }

    public static void CloseCurrent() => s_current?.Close();

    /// <summary>A fresh review: whatever was browsed before is dropped, not left behind a back button.</summary>
    private void ShowProfile(string actor)
    {
        ContentFrame.Navigate(typeof(ProfilePage), new ContainedProfileRequest(actor));
        ContentFrame.BackStack.Clear();
        BackButton.Visibility = Visibility.Collapsed;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack)
            ContentFrame.GoBack();
    }

    private void OnPosted(object? sender, EventArgs e) => Close();

    /// <summary>Beside the tray popup with a little space, never over it; in the work area's corner if the popup isn't up.</summary>
    private void SizeAndPlace()
    {
        this.SetWindowSize(400, 640);

        RectInt32 work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        uint dpi = PInvoke.GetDpiForWindow((HWND)WindowNative.GetWindowHandle(this));
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        int gap = (int)Math.Round(Gap * scale);
        SizeInt32 size = AppWindow.Size;

        PixelRect anchor = App.Current.PopupBounds
            ?? new PixelRect(work.X + work.Width - gap, work.Y + work.Height - gap, 0, 0);

        (int x, int y) = PopoutPlacementPolicy.Place(
            anchor, new PixelRect(work.X, work.Y, work.Width, work.Height), size.Width, size.Height, gap);
        AppWindow.Move(new PointInt32(x, y));
    }
}
