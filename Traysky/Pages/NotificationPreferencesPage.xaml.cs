using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Traysky.ViewModels;

namespace Traysky.Pages;

/// <summary>
/// Bluesky's per-type notification settings, reached from Settings. Not the same as the Windows
/// notification toggles on the Settings page, which only decide what Traysky pops up locally.
/// </summary>
public sealed partial class NotificationPreferencesPage : Page
{
    public NotificationPreferencesViewModel ViewModel { get; } = new();

    public NotificationPreferencesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    /// <summary>Retry only helps when the load failed; a refused save already put the toggle back.</summary>
    public Visibility RetryVisibility(bool isReady) => isReady ? Visibility.Collapsed : Visibility.Visible;
}
