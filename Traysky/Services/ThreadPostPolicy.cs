using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>Rules for a multi-post thread written in the compose box. Pure so it can be tested.</summary>
public static class ThreadPostPolicy
{
    /// <summary>bsky.app's composer allows up to 25 posts in one thread, the first included.</summary>
    public const int MaxPosts = 25;

    /// <summary>Whether another post can be added, given how many there are now (the first included).</summary>
    public static bool CanAdd(int totalPosts) => totalPosts < MaxPosts;

    /// <summary>The texts to send after the first post: trimmed, with blank boxes skipped.</summary>
    public static IReadOnlyList<string> SendableTexts(IEnumerable<string?> texts) =>
        texts.Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0).ToList();

    /// <summary>A follow-on post goes out if it has any text or any media; an empty box is skipped.</summary>
    public static bool IsSendable(string? text, int attachmentCount) =>
        !string.IsNullOrWhiteSpace(text) || attachmentCount > 0;

    /// <summary>The key of the counter colour for a post's text.</summary>
    public static string CounterBrushKey(string? text) =>
        PostTextPolicy.IsOverLimit(text) ? "SystemFillColorCriticalBrush"
        : PostTextPolicy.IsNearLimit(text) ? "SystemFillColorCautionBrush"
        : "TextFillColorSecondaryBrush";

    /// <summary>Whether every extra post fits Bluesky's length limit.</summary>
    public static bool AllFit(IEnumerable<string?> texts) => texts.All(t => !PostTextPolicy.IsOverLimit(t));

    /// <summary>"Posted 2 of 4. ..." for a thread that stopped partway; <paramref name="posted"/> counts the first post.</summary>
    public static string PartialFailureNote(int posted, int total) =>
        $"Posted {posted} of {total}. The rest are still here and will go out as a new thread.";
}
