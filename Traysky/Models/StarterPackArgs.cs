namespace Traysky.Models;

/// <summary>Navigation parameter for the starter pack detail page.</summary>
/// <param name="ListUri">AT URI of the list holding the pack's members.</param>
/// <param name="Name">Pack name, shown as the heading.</param>
/// <param name="Description">Pack description, possibly empty.</param>
/// <param name="CreatorHandle">Handle of the account that made the pack.</param>
/// <param name="WebUrl">The pack's bsky.app link, for "Open on bsky.app".</param>
public sealed record StarterPackArgs(string ListUri, string Name, string Description, string CreatorHandle, string WebUrl);
