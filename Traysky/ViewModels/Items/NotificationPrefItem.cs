using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One notification type on the settings screen: whether Bluesky lists it, pushes it, and (for
/// types that allow it) only for people you follow. Edits raise <see cref="Edited"/> unless they
/// come from <see cref="Set"/>, which the view model uses for loading and for undoing a failed save.
/// </summary>
public sealed partial class NotificationPrefItem(NotificationPrefRow row) : ObservableObject
{
    private bool _quiet;

    public NotificationPrefRow Row { get; } = row;

    public string Label => Row.Label;

    public string Description => Row.Description;

    public bool CanLimitToFollows => Row.CanLimitToFollows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool ShowInList { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Push { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool FollowsOnly { get; set; }

    public string Summary => NotificationPreferencePolicy.Summary(ShowInList, Push, FollowsOnly);

    /// <summary>Raised when the user changes a toggle.</summary>
    public event Action<NotificationPrefItem>? Edited;

    public void Set(bool showInList, bool push, bool followsOnly)
    {
        _quiet = true;
        try
        {
            ShowInList = showInList;
            Push = push;
            FollowsOnly = followsOnly && CanLimitToFollows;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnShowInListChanged(bool value) => RaiseEdited();

    partial void OnPushChanged(bool value) => RaiseEdited();

    partial void OnFollowsOnlyChanged(bool value) => RaiseEdited();

    private void RaiseEdited()
    {
        if (!_quiet)
            Edited?.Invoke(this);
    }
}
