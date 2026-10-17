using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>One line of the "Your feeds" screen: a saved feed, list or Following, and what can be done with it. Rebuilt after every change.</summary>
public sealed class SavedFeedRow(SavedFeedEntry entry, SavedFeedLabel? label, bool canMoveUp, bool canMoveDown)
{
    public SavedFeedEntry Entry { get; } = entry;

    public string Name { get; } = entry.Type switch
    {
        SavedFeedPolicy.TimelineType => "Following",
        _ => label?.Name ?? (entry.Type == SavedFeedPolicy.FeedType || entry.Type == SavedFeedPolicy.ListType ? "Unavailable feed" : "Other feed")
    };

    public Uri? AvatarUri { get; } = label?.AvatarUri;

    public string KindLabel => Entry.Type switch
    {
        SavedFeedPolicy.TimelineType => "Your timeline",
        SavedFeedPolicy.ListType => Entry.Pinned ? "List · pinned" : "List · saved",
        SavedFeedPolicy.FeedType => Entry.Pinned ? "Feed · pinned" : "Feed · saved",
        _ => Entry.Pinned ? "Pinned" : "Saved"
    };

    public bool IsPinned => Entry.Pinned;

    public string PinLabel => SavedFeedPolicy.PinLabel(Entry.Pinned);

    public bool CanMoveUp { get; } = canMoveUp;

    public bool CanMoveDown { get; } = canMoveDown;

    /// <summary>Following is always there; everything else can be dropped from the saved list.</summary>
    public bool CanRemove => Entry.Type != SavedFeedPolicy.TimelineType;
}
