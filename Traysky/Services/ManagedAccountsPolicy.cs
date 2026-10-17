using System;

namespace Traysky.Services;

/// <summary>Decisions for the blocked / muted accounts screen. Pure .NET on purpose.</summary>
public static class ManagedAccountsPolicy
{
    /// <summary>
    /// Where a row goes back when its optimistic removal fails: its old slot, or the end if the
    /// list has shrunk (or been reloaded) since.
    /// </summary>
    public static int RestoreIndex(int removedIndex, int currentCount) =>
        Math.Clamp(removedIndex, 0, Math.Max(0, currentCount));

    public static string EmptyText(bool blocks) => blocks ? "You haven't blocked anyone" : "You haven't muted anyone";

    public static string ActionLabel(bool blocks) => blocks ? "Unblock" : "Unmute";

    public static string FailureText(bool blocks, string handle) =>
        $"Couldn't {(blocks ? "unblock" : "unmute")} @{handle}. Try again.";
}
