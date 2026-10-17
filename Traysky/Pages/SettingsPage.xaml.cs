using Microsoft.UI.Xaml.Controls;
using Traysky.ViewModels;

namespace Traysky.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void EditProfile_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Services.NavigationService.Instance.Navigate(typeof(EditProfilePage));

    private void MyFeeds_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Services.NavigationService.Instance.Navigate(typeof(MyFeedsPage));

    private void BlockedMuted_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Services.NavigationService.Instance.Navigate(typeof(BlockedMutedPage));

    private void NotificationPreferences_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Services.NavigationService.Instance.Navigate(typeof(NotificationPreferencesPage));

    private void ActivitySubscriptions_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        Services.NavigationService.Instance.Navigate(typeof(ActivitySubscriptionsPage));

    public string AtHandle(string? handle) => string.IsNullOrEmpty(handle) ? "Not signed in" : "@" + handle;

    public Microsoft.UI.Xaml.Visibility TextVisibility(string? text) => string.IsNullOrEmpty(text) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
}
