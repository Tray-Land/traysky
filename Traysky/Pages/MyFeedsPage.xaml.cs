using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Traysky.ViewModels;
using Traysky.ViewModels.Items;

namespace Traysky.Pages;

/// <summary>Pin, unpin, reorder and remove the account's saved feeds. Reached from Settings.</summary>
public sealed partial class MyFeedsPage : Page
{
    public MyFeedsViewModel ViewModel { get; } = new();

    public MyFeedsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    public bool Not(bool value) => !value;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedFeedRow row })
            _ = ViewModel.MoveAsync(row, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedFeedRow row })
            _ = ViewModel.MoveAsync(row, 1);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedFeedRow row })
            _ = ViewModel.PinOrUnpinAsync(row);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SavedFeedRow row })
            _ = ViewModel.RemoveAsync(row);
    }
}
