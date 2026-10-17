using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Chat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;
using ChatProfile = idunno.Bluesky.Chat.Actor.ProfileViewBasic;
using GroupJoinRule = idunno.Bluesky.Chat.Group.JoinRule;
using GroupLinkEnabledStatus = idunno.Bluesky.Chat.Group.LinkEnabledStatus;

namespace Traysky.Services;

/// <summary>One member of a group, and whether they own it.</summary>
public sealed record GroupMember(ActorItem Actor, bool IsOwner);

/// <summary>A page of a group's members; <see cref="Cursor"/> is null at the end.</summary>
public sealed record GroupMemberPage(IReadOnlyList<GroupMember> Members, string? Cursor);

/// <summary>A group's join link as the owner sees it.</summary>
public sealed record GroupJoinLink(string Code, bool Enabled, bool RequireApproval, bool FollowedByOwnerOnly);

/// <summary>A group as last fetched: the conversation row, its join link (owner only) and how many people are waiting to be let in.</summary>
public sealed record GroupDetail(ConversationItem Conversation, GroupJoinLink? Link, long PendingRequests);

/// <summary>What a join link says about the group behind it. Exactly one of the flags or <see cref="Name"/> tells the story.</summary>
public sealed record GroupJoinPreview(
    string Code,
    string Name,
    string OwnerName,
    long MemberCount,
    long MemberLimit,
    bool RequireApproval,
    bool FollowedByOwnerOnly,
    bool IsDisabled,
    bool IsInvalid,
    bool RequestPending,
    ConversationItem? AlreadyIn);

/// <summary>
/// Group chat calls: create, rename, add and remove people, the join link and its requests, and
/// joining by link. Each returns null (or the value) on success and a sentence for the user on
/// failure, never throws. Results the library may hand back in a wrapper are not relied on;
/// the group is re-read with <see cref="GetDetailAsync"/> instead.
/// </summary>
public static class GroupChatService
{
    private const int MemberPageSize = 50;

    // ---- Create and edit -----------------------------------------------------------------

    /// <summary>Makes a group with <paramref name="memberDids"/> in it (the account itself is added by the server).</summary>
    public static async Task<(ConversationItem? Conversation, string? Error)> CreateAsync(string name, IReadOnlyCollection<string> memberDids)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
                return (null, "You're signed out. Sign in again.");

            var result = await session.Agent.CreateGroup(memberDids.Select(d => new Did(d)).ToList(), name);
            if (!result.Succeeded)
                return (null, Failure("CreateGroup", result.StatusCode, result.AtErrorDetail, "Couldn't create the group."));

            return (result.Result?.Conversation is { } view ? ChatMapper.ToConversationItem(view, session.Did) : null, null);
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"CreateGroup threw: {ex.Message}");
            return (null, "Couldn't create the group.");
        }
    }

    public static Task<string?> RenameAsync(string conversationId, string name) =>
        RunAsync("EditGroup", "Couldn't rename the group.", async agent =>
        {
            var r = await agent.EditGroup(conversationId, name);
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    public static Task<string?> AddMembersAsync(string conversationId, IReadOnlyCollection<string> dids) =>
        RunAsync("AddMembersToGroup", "Couldn't add them to the group.", async agent =>
        {
            var r = await agent.AddMembersToGroup(conversationId, dids.Select(d => new Did(d)).ToList());
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    public static Task<string?> RemoveMemberAsync(string conversationId, string did) =>
        RunAsync("RemoveGroupMembers", "Couldn't remove them from the group.", async agent =>
        {
            var r = await agent.RemoveGroupMembers(conversationId, [new Did(did)]);
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    // ---- Reading a group -----------------------------------------------------------------

    /// <summary>The group's current name, size, lock and (for the owner) join link. Null when it couldn't be read.</summary>
    public static async Task<GroupDetail?> GetDetailAsync(string conversationId)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
                return null;

            AtProtoHttpResult<ConversationView> result = await session.Agent.GetConversation(conversationId);
            if (!result.Succeeded || result.Result is not { } view)
            {
                LogService.Warn("Chat", $"GetConversation failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            ConversationItem item = ChatMapper.ToConversationItem(view, session.Did);
            if (view.Kind is not GroupConversation group)
                return new GroupDetail(item, null, 0);

            GroupJoinLink? link = group.JoinLink is { } joinLink
                ? new GroupJoinLink(
                    joinLink.Code,
                    string.Equals(joinLink.EnabledStatus, GroupLinkEnabledStatus.Enabled, StringComparison.Ordinal),
                    joinLink.RequireApproval == true,
                    string.Equals(joinLink.JoinRule, GroupJoinRule.FollowedByOwner, StringComparison.Ordinal))
                : null;

            return new GroupDetail(item, link, Convert.ToInt64(group.JoinRequestCount));
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"GetConversation threw: {ex.Message}");
            return null;
        }
    }

    /// <summary>One page of the group's members. Null when the call failed.</summary>
    public static async Task<GroupMemberPage?> GetMembersAsync(string conversationId, string? cursor)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
                return null;

            var result = await session.Agent.GetConversationMembers(conversationId, MemberPageSize, cursor);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Chat", $"GetConversationMembers failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            List<GroupMember> members = [];
            foreach (object? entry in (IEnumerable)(object)result.Result)
            {
                if (entry is ChatProfile profile)
                    members.Add(new GroupMember(ChatMapper.ToActorItem(profile), ChatMapper.IsOwnerRole(profile)));
            }

            return new GroupMemberPage(members, result.Result.Cursor);
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"GetConversationMembers threw: {ex.Message}");
            return null;
        }
    }

    // ---- Join link (owner) ---------------------------------------------------------------

    public static Task<string?> CreateLinkAsync(string conversationId, bool requireApproval, bool followedByOwnerOnly) =>
        RunAsync("CreateJoinGroupLink", "Couldn't create the join link.", async agent =>
        {
            var r = await agent.CreateJoinGroupLink(conversationId, requireApproval, RuleFor(followedByOwnerOnly));
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    public static Task<string?> SetLinkEnabledAsync(string conversationId, bool enabled) =>
        RunAsync(enabled ? "EnableJoinGroupLink" : "DisableJoinGroupLink", "Couldn't change the join link.", async agent =>
        {
            if (enabled)
            {
                var on = await agent.EnableJoinGroupLink(conversationId);
                return (on.Succeeded, on.StatusCode, on.AtErrorDetail);
            }

            var off = await agent.DisableJoinGroupLink(conversationId);
            return (off.Succeeded, off.StatusCode, off.AtErrorDetail);
        });

    public static Task<string?> EditLinkAsync(string conversationId, bool requireApproval, bool followedByOwnerOnly) =>
        RunAsync("EditJoinGroupLink", "Couldn't change the join link.", async agent =>
        {
            var r = await agent.EditJoinGroupLink(conversationId, requireApproval, RuleFor(followedByOwnerOnly));
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    /// <summary>The people waiting to be let in, oldest call first. Opening the list also marks the requests read.</summary>
    public static async Task<List<ActorItem>?> GetJoinRequestsAsync(string conversationId)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
                return null;

            var result = await session.Agent.ListJoinGroupRequests(conversationId, limit: 100);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Chat", $"ListJoinGroupRequests failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            List<ActorItem> people = [];
            foreach (object? entry in (IEnumerable)(object)result.Result)
            {
                if (entry is idunno.Bluesky.Chat.Group.JoinRequestView { RequestedBy: { } who })
                    people.Add(ChatMapper.ToActorItem(who));
            }

            if (people.Count > 0)
                _ = session.Agent.UpdateJoinGroupRequestsRead(conversationId);

            return people;
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"ListJoinGroupRequests threw: {ex.Message}");
            return null;
        }
    }

    public static Task<string?> ApproveRequestAsync(string conversationId, string did) =>
        RunAsync("ApproveJoinGroupRequest", "Couldn't approve the request.", async agent =>
        {
            var r = await agent.ApproveJoinGroupRequest(conversationId, new Did(did));
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    public static Task<string?> RejectRequestAsync(string conversationId, string did) =>
        RunAsync("RejectJoinGroupRequest", "Couldn't reject the request.", async agent =>
        {
            var r = await agent.RejectJoinGroupRequest(conversationId, new Did(did));
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

    // ---- Joining by link -----------------------------------------------------------------

    /// <summary>What a join code points at. Null when the call couldn't be made.</summary>
    public static async Task<GroupJoinPreview?> PreviewAsync(string code)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
                return null;

            var result = await session.Agent.GetJoinGroupLinkPreviews([code]);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Chat", $"GetJoinGroupLinkPreviews failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return null;
            }

            foreach (object? entry in (IEnumerable)(object)result.Result)
            {
                switch (entry)
                {
                    case idunno.Bluesky.Chat.Group.JoinLinkPreviewView p:
                        return new GroupJoinPreview(
                            code,
                            p.Name?.Trim() ?? string.Empty,
                            (object?)p.Owner is ChatProfile owner ? ChatMapper.ToActorItem(owner).NameOrHandle : string.Empty,
                            Convert.ToInt64(p.MemberCount),
                            Convert.ToInt64(p.MemberLimit),
                            p.RequireApproval == true,
                            string.Equals(p.JoinRule, GroupJoinRule.FollowedByOwner, StringComparison.Ordinal),
                            false,
                            false,
                            (object?)p.Viewer is idunno.Bluesky.Chat.Group.JoinLinkViewerState { RequestedAt: not null },
                            p.Conversation is { } view ? ChatMapper.ToConversationItem(view, session.Did) : null);

                    case idunno.Bluesky.Chat.Group.DisabledJoinLinkPreviewView:
                        return new GroupJoinPreview(code, string.Empty, string.Empty, 0, 0, false, false, true, false, false, null);

                    case idunno.Bluesky.Chat.Group.InvalidJoinLinkPreviewView:
                        return new GroupJoinPreview(code, string.Empty, string.Empty, 0, 0, false, false, false, true, false, null);
                }
            }

            return new GroupJoinPreview(code, string.Empty, string.Empty, 0, 0, false, false, false, true, false, null);
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"GetJoinGroupLinkPreviews threw: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Joins, or asks to join, through <paramref name="code"/>. Afterwards the link is previewed again,
    /// which says whether the account is now in (the group is returned) or waiting for approval.
    /// </summary>
    public static async Task<(GroupJoinPreview? After, string? Error)> JoinAsync(string code)
    {
        string? error = await RunAsync("RequestJoinGroup", "Couldn't join the group.", async agent =>
        {
            var r = await agent.RequestJoinGroup(code);
            return (r.Succeeded, r.StatusCode, r.AtErrorDetail);
        });

        if (error is not null)
            return (null, error);

        return (await PreviewAsync(code), null);
    }

    // ---- Plumbing ------------------------------------------------------------------------

    private static string RuleFor(bool followedByOwnerOnly) => followedByOwnerOnly ? GroupJoinRule.FollowedByOwner : GroupJoinRule.Anyone;

    /// <summary>Runs one chat call; null when it worked, else a sentence for the user.</summary>
    private static async Task<string?> RunAsync(string what, string failureText, Func<BlueskyAgent, Task<(bool Ok, HttpStatusCode Status, AtErrorDetail? Detail)>> call)
    {
        try
        {
            BlueskySessionService session = BlueskySessionService.Instance;
            if (!await session.EnsureAuthenticatedAsync())
                return "You're signed out. Sign in again.";

            (bool ok, HttpStatusCode status, AtErrorDetail? detail) = await call(session.Agent);
            return ok ? null : Failure(what, status, detail, failureText);
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"{what} threw: {ex.Message}");
            return failureText;
        }
    }

    private static string Failure(string what, HttpStatusCode status, AtErrorDetail? detail, string fallback)
    {
        LogService.Warn("Chat", $"{what} failed: {(int)status} {detail?.Error}");
        return status == 0
            ? "Couldn't reach Bluesky. Check your connection."
            : detail?.Message is { Length: > 0 } message ? message : fallback;
    }
}
