using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Traysky.Services;
using Traysky.ViewModels;

namespace Traysky.Pages;

/// <summary>Edit the account's own display name, bio, avatar and banner. Reached from Settings.</summary>
public sealed partial class EditProfilePage : Page
{
    public EditProfileViewModel ViewModel { get; } = new();

    public EditProfilePage()
    {
        InitializeComponent();
        ViewModel.Saved += (_, _) => NavigationService.Instance.GoBack();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    /// <summary>Retry only helps when the load failed; a failed save keeps the form as it is.</summary>
    public Visibility RetryVisibility(bool isReady) => isReady ? Visibility.Collapsed : Visibility.Visible;
}
