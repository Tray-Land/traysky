using idunno.AtProto;
using idunno.Bluesky.Graph;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>A page of muted / blocked lists; see <see cref="ListsService.GetManagedListsAsync"/>.</summary>
public sealed record ManagedListsPage(System.Net.HttpStatusCode Status, List<ListItem>? Items, string? Cursor);

/// <summary>
/// Create, edit and delete lists and manage their members. Each call returns what the server
/// handed back (or null / false when it refused) and logs the reason, so callers only decide how
/// to show the outcome.
/// </summary>
public static class ListsService
{
    /// <summary>Makes a list and returns its row, or null on failure.</summary>
    public static async Task<ListItem?> CreateAsync(string name, string description, ListKind kind)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        try
        {
            string trimmed = description.Trim();
            var list = new idunno.Bluesky.Graph.List(name, FeedMapper.ToListPurpose(kind), trimmed.Length > 0 ? trimmed : null);
            var result = await BlueskySessionService.Instance.Agent.CreateList(list);
            if (result.Succeeded && result.Result is not null)
            {
                return new ListItem
                {
                    Uri = result.Result.Uri.ToString(),
                    Name = name,
                    Description = trimmed,
                    Kind = kind,
                    CreatorDid = BlueskySessionService.Instance.Did ?? string.Empty,
                    CreatorHandle = BlueskySessionService.Instance.Handle ?? string.Empty
                };
            }

            LogService.Warn("Lists", $"CreateList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"CreateList threw: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Renames a list and/or changes its description. The purpose can't change after creation. The
    /// record is read first and written back with only the text changed, so the avatar and labels survive.
    /// </summary>
    public static async Task<bool> UpdateAsync(string listUri, string name, string description)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            var current = await agent.GetListRecord(new AtUri(listUri), System.Threading.CancellationToken.None);
            if (!current.Succeeded || current.Result is null)
            {
                LogService.Warn("Lists", $"GetListRecord failed: {(int)current.StatusCode} {current.AtErrorDetail?.Error}");
                return false;
            }

            string trimmed = description.Trim();
            var record = current.Result;
            record.Value.Name = name;
            record.Value.Description = trimmed.Length > 0 ? trimmed : null;
            // Facets index into the old text, so they would point at the wrong bytes now.
            record.Value.DescriptionFacets = null;

            var result = await agent.UpdateList(record);
            if (result.Succeeded)
                return true;

            LogService.Warn("Lists", $"UpdateList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"UpdateList threw: {ex.Message}");
        }
        return false;
    }

    public static async Task<bool> DeleteAsync(string listUri)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.DeleteList(new AtUri(listUri));
            if (result.Succeeded)
                return true;

            LogService.Warn("Lists", $"DeleteList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"DeleteList threw: {ex.Message}");
        }
        return false;
    }

    /// <summary>Puts the account on the list; returns the list-item record's AT URI (needed to undo it), or null on failure.</summary>
    public static async Task<string?> AddMemberAsync(string listUri, string did)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.AddToList(new AtUri(listUri), new Did(did));
            if (result.Succeeded && result.Result is not null)
                return result.Result.Uri.ToString();

            LogService.Warn("Lists", $"AddToList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"AddToList threw: {ex.Message}");
        }
        return null;
    }

    /// <summary>Takes a member off a list, by the list-item record that put them there.</summary>
    public static async Task<bool> RemoveMemberAsync(string listItemUri)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.DeleteFromList(new AtUri(listItemUri));
            if (result.Succeeded)
                return true;

            LogService.Warn("Lists", $"DeleteFromList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"DeleteFromList threw: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Mutes or unmutes everyone on a list: a moderation list through the mod-list calls, a curation
    /// list through the actor-list ones (see <see cref="MuteTargetPolicy.UsesActorListCall"/>). True when the server accepted it.
    /// </summary>
    public static async Task<bool> SetListMutedAsync(string listUri, bool muted, ListKind kind = ListKind.Moderation)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            var uri = new AtUri(listUri);
            bool actorList = MuteTargetPolicy.UsesActorListCall(kind);
            var result = (actorList, muted) switch
            {
                (true, true) => await agent.MuteActorList(uri),
                (true, false) => await agent.UnmuteActorList(uri),
                (false, true) => await agent.MuteModList(uri),
                (false, false) => await agent.UnmuteModList(uri)
            };
            if (result.Succeeded)
                return true;

            LogService.Warn("Lists", $"{(muted ? "Mute" : "Unmute")}{(actorList ? "ActorList" : "ModList")} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"Mute list toggle threw: {ex.Message}");
        }
        return false;
    }

    /// <summary>Blocks or unblocks everyone on a moderation list. True when the server accepted it.</summary>
    public static async Task<bool> SetListBlockedAsync(string listUri, bool blocked)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            if (blocked)
            {
                var block = await agent.BlockModList(new AtUri(listUri));
                if (block.Succeeded)
                    return true;

                LogService.Warn("Lists", $"BlockModList failed: {(int)block.StatusCode} {block.AtErrorDetail?.Error}");
            }
            else
            {
                var unblock = await agent.UnblockModList(new AtUri(listUri));
                if (unblock.Succeeded)
                    return true;

                LogService.Warn("Lists", $"UnblockModList failed: {(int)unblock.StatusCode} {unblock.AtErrorDetail?.Error}");
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"Block list toggle threw: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// One page of the moderation lists the signed-in account has muted (<paramref name="blocks"/> false)
    /// or blocked. <c>Items</c> is null when the lookup failed; <c>Status</c> is the HTTP status (0 when
    /// the server wasn't reached).
    /// </summary>
    public static async Task<ManagedListsPage> GetManagedListsAsync(bool blocks, string? cursor, int limit)
    {
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            var agent = BlueskySessionService.Instance.Agent;
            var result = blocks
                ? await agent.GetListBlocks(limit, cursor, LabelService.Instance.Subscribed)
                : await agent.GetListMutes(limit, cursor, LabelService.Instance.Subscribed);

            if (result.Succeeded && result.Result is not null)
                return new ManagedListsPage(result.StatusCode, result.Result.Select(FeedMapper.ToListItem).ToList(), result.Result.Cursor);

            LogService.Warn("Lists", $"{(blocks ? "GetListBlocks" : "GetListMutes")} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            return new ManagedListsPage(result.StatusCode, null, null);
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"{(blocks ? "GetListBlocks" : "GetListMutes")} threw: {ex.Message}");
            return new ManagedListsPage(0, null, null);
        }
    }

    /// <summary>The curation lists the signed-in account owns, for picking who may reply. Null when the lookup failed.</summary>
    public static async Task<List<ListItem>?> GetOwnCurationListsAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn || BlueskySessionService.Instance.Did is not { } did)
            return null;

        List<ListItem> all = [];
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            string? cursor = null;
            do
            {
                var result = await BlueskySessionService.Instance.Agent.GetLists(new Did(did), limit: 100, cursor: cursor, subscribedLabelers: LabelService.Instance.Subscribed);
                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("Lists", $"GetLists failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    return null;
                }

                all.AddRange(result.Result.Select(FeedMapper.ToListItem).Where(l => l.Kind == ListKind.Curate));
                cursor = result.Result.Cursor;
            }
            while (!string.IsNullOrEmpty(cursor));
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"GetLists threw: {ex.Message}");
            return null;
        }
        return all;
    }

    /// <summary>
    /// Every list the signed-in account owns, each with the list-item record that puts
    /// <paramref name="did"/> on it (null when they aren't). Null when the lookup failed.
    /// </summary>
    public static async Task<List<ListMembershipItem>?> GetMembershipsAsync(string did)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        List<ListMembershipItem> all = [];
        try
        {
            string? cursor = null;
            do
            {
                var result = await BlueskySessionService.Instance.Agent.GetListsWithMembership(new Did(did), limit: 100, cursor: cursor);
                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("Lists", $"GetListsWithMembership failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    return null;
                }

                foreach (ListWithMembership entry in result.Result)
                {
                    ListItem item = FeedMapper.ToListItem(entry.List);
                    if (ListPolicy.IsShown(item.Kind))
                        all.Add(new ListMembershipItem(item, entry.ListItem?.Uri.ToString()));
                }

                cursor = result.Result.Cursor;
            }
            while (!string.IsNullOrEmpty(cursor));
        }
        catch (Exception ex)
        {
            LogService.Warn("Lists", $"GetListsWithMembership threw: {ex.Message}");
            return null;
        }
        return all;
    }
}
