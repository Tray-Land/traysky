using CommunityToolkit.Mvvm.ComponentModel;

namespace Traysky.ViewModels.Items;

/// <summary>
/// The "Load more" row between a thread's first post and the post a followed user replied to,
/// when posts in between are not shown.
/// </summary>
public sealed partial class ThreadGapItem : ObservableObject
{
    /// <summary>The followed user's reply the gap sits above; opened as a full thread when the gap is long.</summary>
    public PostItem Reply { get; init; } = null!;

    /// <summary>AT URI of the thread's first post (shown above the gap).</summary>
    public string RootUri { get; init; } = string.Empty;

    /// <summary>AT URI of the post replied to (shown below the gap).</summary>
    public string ParentUri { get; init; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }
}
