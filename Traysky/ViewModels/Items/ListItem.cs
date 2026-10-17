using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>One curation or moderation list, projected from <c>ListView</c> (see <see cref="FeedMapper.ToListItem"/>).</summary>
public sealed class ListItem
{
    /// <summary>The list's <c>app.bsky.graph.list</c> AT URI.</summary>
    public string Uri { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ListKind Kind { get; init; } = ListKind.Curate;
    public Uri? AvatarUri { get; init; }
    public int MemberCount { get; init; }

    public string CreatorDid { get; init; } = string.Empty;
    public string CreatorHandle { get; init; } = string.Empty;

    /// <summary>The signed-in account mutes everyone on this (moderation) list.</summary>
    public bool IsMuted { get; init; }

    /// <summary>The signed-in account blocks everyone on this (moderation) list.</summary>
    public bool IsBlocked { get; init; }

    /// <summary>The row button on the Blocked &amp; muted screen ("Unmute" / "Unblock"); empty elsewhere.</summary>
    public string ManagedActionLabel { get; set; } = string.Empty;

    public bool HasDescription => Description.Length > 0;

    public bool IsMine => ListPolicy.CanManage(CreatorDid, PreviewMode.IsEnabled ? PreviewMode.SelfDid : BlueskySessionService.Instance.Did);

    public string Subtitle => ListPolicy.Subtitle(Kind, MemberCount);

    /// <summary>"Curation list · 12 accounts", with the creator when it isn't yours.</summary>
    public string ByLine => IsMine || CreatorHandle.Length == 0 ? Subtitle : $"{Subtitle} · by @{CreatorHandle}";

    public string WebUrl
    {
        get
        {
            int slash = Uri.LastIndexOf('/');
            return slash < 0 || CreatorDid.Length == 0 ? string.Empty : $"https://bsky.app/profile/{CreatorDid}/lists/{Uri[(slash + 1)..]}";
        }
    }
}

/// <summary>A list in the "Add to list" dialog: whether the account is on it, and the list-item record to delete to take them off.</summary>
public sealed partial class ListMembershipItem : ObservableObject
{
    public ListMembershipItem(ListItem list, string? membershipUri)
    {
        List = list;
        MembershipUri = membershipUri;
        IsMember = membershipUri is not null;
    }

    public ListItem List { get; }

    /// <summary>The <c>app.bsky.graph.listitem</c> record that puts the account on the list; null when they aren't on it.</summary>
    public string? MembershipUri { get; set; }

    [ObservableProperty]
    public partial bool IsMember { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }
}
