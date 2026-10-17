using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// One chat notification type on the settings screen: whether Bluesky pushes it, and whether only
/// for people you follow. Edits raise <see cref="Edited"/> unless they come from <see cref="Set"/>,
/// which the view model uses for loading and for undoing a failed save.
/// </summary>
public sealed partial class ChatPrefItem(ChatPrefRow row) : ObservableObject
{
    private bool _quiet;

    public ChatPrefRow Row { get; } = row;

    public string Label => Row.Label;

    public string Description => Row.Description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool Push { get; set; } = ChatNotificationPreferencePolicy.DefaultPush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial bool FollowsOnly { get; set; } = ChatNotificationPreferencePolicy.DefaultFollowsOnly;

    public string Summary => ChatNotificationPreferencePolicy.Summary(Push, FollowsOnly);

    public event Action<ChatPrefItem>? Edited;

    public void Set(bool push, bool followsOnly)
    {
        _quiet = true;
        try
        {
            Push = push;
            FollowsOnly = followsOnly;
        }
        finally
        {
            _quiet = false;
        }
    }

    partial void OnPushChanged(bool value) => RaiseEdited();

    partial void OnFollowsOnlyChanged(bool value) => RaiseEdited();

    private void RaiseEdited()
    {
        if (!_quiet)
            Edited?.Invoke(this);
    }
}
