using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Embed;
using idunno.Bluesky.Feed;
using idunno.Bluesky.Notifications;
using idunno.Bluesky.RichText;
using System;
using System.Collections.Generic;
using System.Linq;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Projects idunno.Bluesky's wire types onto the app's item models. The only place that
/// knows both shapes, so a library upgrade touches one file.
/// </summary>
public static class FeedMapper
{
    public static PostItem ToPostItem(FeedViewPost feedPost)
    {
        string? repostedBy = feedPost.Reason is ReasonRepost repost
            ? DisplayNameOf(repost.By)
            : null;

        string? replyingTo = feedPost.Reply?.Parent is PostView parent
            ? parent.Author?.Handle?.ToString()
            : null;

        return ToPostItem(feedPost.Post, repostedBy, replyingTo);
    }

    /// <summary>
    /// A feed entry as the timeline's rows: for a reply (when <paramref name="includeContext"/>),
    /// the thread's first post, a <see cref="ThreadGapItem"/> if posts in between are hidden, and
    /// the post replied to, each joined to the next by a connector line, then the reply itself.
    /// Anything else (or a reply whose parent is deleted or blocked) is just the post.
    /// </summary>
    public static List<object> ToTimelineRows(FeedViewPost feedPost, bool includeContext)
    {
        PostItem main = ToPostItem(feedPost);
        List<object> rows = [];

        // A repost shows the reposted post as it is, with no thread above it.
        if (includeContext
            && feedPost.Reason is not ReasonRepost
            && feedPost.Reply?.Parent is PostView parent)
        {
            PostView? root = feedPost.Reply.Root as PostView;
            string parentUri = parent.Uri.ToString();
            string? rootUri = root?.Uri.ToString();

            ReplyContextShape shape = ReplyContextPolicy.Classify(rootUri, parentUri, parent.Record?.Reply?.Parent?.Uri?.ToString());

            if (shape is ReplyContextShape.RootAndParent or ReplyContextShape.RootGapParent)
                rows.Add(ContextPost(root!, lineAbove: false));

            if (shape == ReplyContextShape.RootGapParent)
                rows.Add(new ThreadGapItem { Reply = main, RootUri = rootUri!, ParentUri = parentUri });

            // Every shape but None ends with the post replied to, then the reply.
            if (shape != ReplyContextShape.None)
            {
                rows.Add(ContextPost(parent, lineAbove: rows.Count > 0));
                main.ThreadLineAbove = true;
            }
        }

        rows.Add(main);
        return rows;
    }

    /// <summary>
    /// A post shown only as context for a reply below it. Never labelled "Replying to": the line
    /// to the row above says so, and a parent's own parent handle isn't in the feed anyway.
    /// </summary>
    public static PostItem ContextPost(PostView post, bool lineAbove)
    {
        PostItem item = ToPostItem(post, replyingTo: string.Empty);
        item.ThreadLineAbove = lineAbove;
        item.ThreadLineBelow = true;
        return item;
    }

    public static PostItem ToPostItem(PostView post, string? repostedBy = null, string? replyingTo = null)
    {
        Post? record = post.Record;
        string text = record?.Text ?? string.Empty;
        ProfileViewBasic author = post.Author;
        string handle = author.Handle?.ToString() ?? string.Empty;
        string atUri = post.Uri.ToString();

        return new PostItem
        {
            AtUri = atUri,
            Cid = post.Cid.ToString(),
            Reference = post.StrongReference,
            AuthorDid = author.Did.ToString(),
            AuthorHandle = handle,
            AuthorDisplayName = DisplayNameOf(author),
            AvatarUri = author.Avatar,
            AuthorLabels = LabelService.Instance.ForAccount(author),
            Text = text,
            Segments = FacetSegmenter.Segment(text, ToFacetInputs(record?.Facets)),
            CreatedAt = record?.CreatedAt ?? post.IndexedAt,
            RepostedBy = repostedBy,
            ReplyingTo = replyingTo ?? ReplyingToFromRecord(record),
            Embed = ToEmbedItem(post.Embed),
            WebUrl = BlueskyLinks.PostUrl(atUri, handle),
            LikeCount = post.LikeCount,
            RepostCount = post.RepostCount,
            ReplyCount = post.ReplyCount,
            QuoteCount = post.QuoteCount,
            IsLiked = post.Viewer?.Like is not null,
            IsReposted = post.Viewer?.Repost is not null,
            IsBookmarked = post.Viewer?.Bookmarked == true,
            ThreadRootUri = PostMenuPolicy.ThreadRootUri(atUri, record?.Reply?.Root?.Uri?.ToString()),
            IsThreadMuted = post.Viewer?.ThreadMuted == true,
            LikeUri = post.Viewer?.Like?.ToString(),
            RepostUri = post.Viewer?.Repost?.ToString()
        };
    }

    public static string DisplayNameOf(ProfileViewBasic? profile)
    {
        if (profile is null)
            return string.Empty;

        return string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.Handle?.ToString() ?? string.Empty
            : profile.DisplayName.Trim();
    }

    /// <summary>Projects a typeahead search result onto the ComposeBox '@' suggestion popup's item shape.</summary>
    public static MentionSuggestionItem ToMentionSuggestion(ProfileViewBasic profile) => new()
    {
        Did = profile.Did.ToString(),
        Handle = profile.Handle?.ToString() ?? string.Empty,
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? string.Empty : profile.DisplayName.Trim(),
        AvatarUri = profile.Avatar
    };

    /// <summary>Projects an account onto a people-list row (who liked / reposted a post).</summary>
    public static ActorItem ToActorItem(ProfileViewBasic profile) => new()
    {
        Did = profile.Did.ToString(),
        Handle = profile.Handle?.ToString() ?? string.Empty,
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? string.Empty : profile.DisplayName.Trim(),
        AvatarUri = profile.Avatar
    };

    /// <summary>Projects a starter pack search hit onto a result row.</summary>
    public static StarterPackItem ToStarterPackItem(idunno.Bluesky.Graph.StarterPackViewBasic pack)
    {
        string uri = pack.Uri.ToString();
        string creator = pack.Creator.Handle?.ToString() ?? string.Empty;
        string authority = pack.Creator.Did.ToString();
        string rkey = uri[(uri.LastIndexOf('/') + 1)..];

        return new StarterPackItem
        {
            Name = pack.Record.Name,
            Description = pack.Record.Description?.Trim() ?? string.Empty,
            CreatorHandle = creator,
            CreatorAvatarUri = pack.Creator.Avatar,
            AtUri = uri,
            ListUri = pack.Record.List.ToString(),
            MemberCount = Convert.ToInt32(pack.ListItemCount),
            JoinedCount = Convert.ToInt32(pack.JoinedAllTimeCount),
            WebUrl = $"https://bsky.app/starter-pack/{authority}/{rkey}"
        };
    }

    public static ListKind ToListKind(idunno.Bluesky.Graph.ListPurpose purpose) => purpose switch
    {
        idunno.Bluesky.Graph.ListPurpose.CurateList => ListKind.Curate,
        idunno.Bluesky.Graph.ListPurpose.ModList => ListKind.Moderation,
        idunno.Bluesky.Graph.ListPurpose.ReferenceList => ListKind.Reference,
        _ => ListKind.Unknown
    };

    public static idunno.Bluesky.Graph.ListPurpose ToListPurpose(ListKind kind) => kind switch
    {
        ListKind.Moderation => idunno.Bluesky.Graph.ListPurpose.ModList,
        ListKind.Reference => idunno.Bluesky.Graph.ListPurpose.ReferenceList,
        _ => idunno.Bluesky.Graph.ListPurpose.CurateList
    };

    /// <summary>Projects a list onto a row (a profile's Lists tab, the "Add to list" dialog).</summary>
    public static ListItem ToListItem(idunno.Bluesky.Graph.ListView list) => new()
    {
        Uri = list.Uri.ToString(),
        Name = list.Name,
        Description = list.Description?.Trim() ?? string.Empty,
        Kind = ToListKind(list.Purpose),
        AvatarUri = list.Avatar,
        MemberCount = list.ListItemCount ?? 0,
        CreatorDid = list.Creator.Did.ToString(),
        CreatorHandle = list.Creator.Handle?.ToString() ?? string.Empty,
        IsMuted = list.Viewer?.Muted == true,
        IsBlocked = list.Viewer?.Blocked is not null
    };

    /// <summary>Projects a feed generator onto a feed row (Explore suggestions, a profile's Feeds tab).</summary>
    public static FeedSuggestionItem ToFeedSuggestionItem(GeneratorView feed)
    {
        string uri = feed.Uri.ToString();
        string rkey = uri[(uri.LastIndexOf('/') + 1)..];
        return new FeedSuggestionItem
        {
            AtUri = uri,
            IsPinned = FeedsService.Instance.IsPinned(uri),
            DisplayName = feed.DisplayName,
            Description = feed.Description?.Trim() ?? string.Empty,
            CreatorHandle = feed.Creator.Handle?.ToString() ?? string.Empty,
            AvatarUri = feed.AvatarUri,
            LikeCount = Convert.ToInt32(feed.LikeCount),
            WebUrl = $"https://bsky.app/profile/{feed.Creator.Did}/feed/{rkey}"
        };
    }

    /// <summary>A row for the blocked / muted screen; <paramref name="blocks"/> picks the button label.</summary>
    public static ActorItem ToManagedActorItem(ProfileView profile, bool blocks) => new()
    {
        ManagedActionLabel = ManagedAccountsPolicy.ActionLabel(blocks),
        Did = profile.Did.ToString(),
        Handle = profile.Handle?.ToString() ?? string.Empty,
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? string.Empty : profile.DisplayName.Trim(),
        AvatarUri = profile.Avatar,
        BlockUri = profile.Viewer?.Blocking?.ToString()
    };

    /// <summary>A row for the notification-bell list; <c>ManagedActionLabel</c> is "Turn off" and the subtitle names what is on.</summary>
    public static ActorItem ToSubscribedActorItem(ProfileView profile)
    {
        ActivitySubscriptionLevel level = profile.Viewer?.ActivitySubscription is { } sub
            ? ActivitySubscriptionPolicy.FromFlags(sub.Post, sub.Reply)
            : ActivitySubscriptionLevel.Posts;

        return new ActorItem
        {
            ManagedActionLabel = "Turn off",
            ManagedDetail = ActivitySubscriptionPolicy.Label(level),
            Did = profile.Did.ToString(),
            Handle = profile.Handle?.ToString() ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? string.Empty : profile.DisplayName.Trim(),
            AvatarUri = profile.Avatar
        };
    }

    public static IEnumerable<FacetInput> ToFacetInputs(IEnumerable<Facet>? facets)
    {
        if (facets is null)
            yield break;

        foreach (Facet facet in facets)
        {
            if (facet.Index is null || facet.Features is null)
                continue;

            foreach (FacetFeature feature in facet.Features)
            {
                switch (feature)
                {
                    case LinkFacetFeature link when link.Uri is not null:
                        yield return new FacetInput(facet.Index.ByteStart, facet.Index.ByteEnd, FacetKind.Link, link.Uri.ToString());
                        break;
                    case MentionFacetFeature mention when mention.Did is not null:
                        yield return new FacetInput(facet.Index.ByteStart, facet.Index.ByteEnd, FacetKind.Mention, mention.Did.ToString());
                        break;
                    case TagFacetFeature tag when !string.IsNullOrEmpty(tag.Tag):
                        yield return new FacetInput(facet.Index.ByteStart, facet.Index.ByteEnd, FacetKind.Tag, tag.Tag);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// A reply's parent handle is not on the record itself (only the parent's AT URI is), so
    /// this only helps when the caller could not supply it from a hydrated ReplyReference.
    /// </summary>
    private static string? ReplyingToFromRecord(Post? record)
    {
        if (record?.Reply?.Parent?.Uri is not AtUri parentUri)
            return null;

        // The DID of the parent's author; better than nothing until the view is hydrated.
        return BlueskyLinks.TryParseAtUri(parentUri.ToString(), out string authority, out _, out _) ? authority : null;
    }

    public static EmbedItem? ToEmbedItem(EmbeddedView? embed)
    {
        switch (embed)
        {
            case null:
                return null;

            case EmbeddedImagesView images:
                return new EmbedItem { Kind = EmbedKind.Images, Images = ToImages(images.Images) };

            case EmbeddedVideoView video:
                return new EmbedItem
                {
                    Kind = EmbedKind.Video,
                    VideoThumbnail = video.ThumbnailUri,
                    VideoPlaylistUri = video.PlaylistUri,
                    VideoAspectRatio = AspectOf(video.AspectRatio),
                    Images = video.ThumbnailUri is null
                        ? []
                        : [new EmbedImage(video.ThumbnailUri, video.ThumbnailUri, video.AltText ?? string.Empty, AspectOf(video.AspectRatio))]
                };

            case EmbeddedExternalView external when external.External is not null:
                {
                    // A link straight to a GIF or picture (bsky.app's GIF picker posts these) is
                    // shown as the image itself, not as a card about it.
                    if (ToExternalImage(external) is EmbedImage gif)
                        return new EmbedItem { Kind = EmbedKind.Images, Images = [gif] };

                    // A link that isn't http(s) still shows its card, just without anything to open.
                    BlueskyLinks.TryParseWebUri(external.External.Uri, out Uri? uri);
                    string? host = uri is null ? null
                        : uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
                    string? title = string.IsNullOrWhiteSpace(external.External.Title) ? host : external.External.Title;
                    if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(external.External.Description))
                        return null;

                    Uri? thumbnail = external.External.ThumbnailUri;
                    return new EmbedItem
                    {
                        Kind = EmbedKind.External,
                        ExternalUri = uri,
                        ExternalHost = host,
                        ExternalTitle = title,
                        ExternalDescription = external.External.Description,
                        ExternalThumbnail = BlueskyLinks.IsWebUri(thumbnail) ? thumbnail : null,
                        OpenUrl = uri?.ToString()
                    };
                }

            case EmbeddedRecordWithMediaView recordWithMedia:
                {
                    // A quote with pictures: show the quote, with the media inside the quote card.
                    EmbedItem? quote = ToQuote(recordWithMedia.Record);
                    EmbedItem? media = ToEmbedItem(recordWithMedia.Media);
                    if (quote is null)
                        return media;

                    if (media is { Kind: EmbedKind.Images or EmbedKind.Video })
                    {
                        return new EmbedItem
                        {
                            Kind = media.Kind,
                            Images = media.Images,
                            VideoThumbnail = media.VideoThumbnail,
                            VideoPlaylistUri = media.VideoPlaylistUri,
                            VideoAspectRatio = media.VideoAspectRatio,
                            QuoteAuthorName = quote.QuoteAuthorName,
                            QuoteAuthorHandle = quote.QuoteAuthorHandle,
                            QuoteAuthorAvatar = quote.QuoteAuthorAvatar,
                            QuoteText = quote.QuoteText,
                            QuoteWebUrl = quote.QuoteWebUrl,
                            QuoteAtUri = quote.QuoteAtUri,
                            QuoteImages = quote.QuoteImages,
                            OpenUrl = quote.OpenUrl
                        };
                    }
                    return quote;
                }

            case EmbeddedRecordView recordView:
                return ToQuote(recordView.Record);

            default:
                return null;
        }
    }

    private static EmbedItem? ToQuote(idunno.Bluesky.Embed.View? record)
    {
        switch (record)
        {
            case ViewRecord viewRecord:
                {
                    string? text = (viewRecord.Value as Post)?.Text;
                    string atUri = viewRecord.Uri.ToString();
                    string? handle = viewRecord.Author?.Handle?.ToString();
                    string? webUrl = BlueskyLinks.PostUrl(atUri, handle);

                    IReadOnlyList<EmbedImage> nestedImages = viewRecord.Embeds?
                        .SelectMany(ToQuotedImages)
                        .ToList() ?? [];

                    return new EmbedItem
                    {
                        Kind = EmbedKind.Quote,
                        QuoteAuthorName = DisplayNameOf(viewRecord.Author),
                        QuoteAuthorHandle = handle,
                        QuoteAuthorAvatar = viewRecord.Author?.Avatar,
                        QuoteText = text,
                        QuoteWebUrl = webUrl,
                        QuoteAtUri = atUri,
                        QuoteImages = nestedImages,
                        OpenUrl = webUrl
                    };
                }

            case ViewNotFound:
            case ViewBlocked:
                return new EmbedItem { Kind = EmbedKind.Unavailable };

            default:
                // Feeds, lists, starter packs and labelers embed too; not worth a card yet.
                return null;
        }
    }

    private static IReadOnlyList<EmbedImage> ToImages(IEnumerable<EmbeddedImageView>? images)
    {
        if (images is null)
            return [];

        List<EmbedImage> list = [];
        foreach (EmbeddedImageView image in images)
        {
            if (image.ThumbnailUri is null)
                continue;
            list.Add(new EmbedImage(image.ThumbnailUri, image.FullSizeUri ?? image.ThumbnailUri, image.AltText ?? string.Empty, AspectOf(image.AspectRatio)));
        }
        return list;
    }

    /// <summary>The pictures inside a quoted post: its images, or a GIF it linked.</summary>
    private static IReadOnlyList<EmbedImage> ToQuotedImages(EmbeddedView embed) => embed switch
    {
        EmbeddedImagesView images => ToImages(images.Images),
        EmbeddedExternalView external when ToExternalImage(external) is EmbedImage gif => [gif],
        _ => []
    };

    private static EmbedImage? ToExternalImage(EmbeddedExternalView external)
    {
        if (external.External is null
            || !ExternalMediaPolicy.TryGetMedia(external.External.Uri, external.External.Title, external.External.Description, out ExternalMedia? media))
            return null;

        return new EmbedImage(media.Uri, media.Uri, media.AltText, media.AspectRatio ?? 16.0 / 9.0);
    }

    private static double AspectOf(AspectRatio? ratio)
    {
        if (ratio is null || ratio.Width <= 0 || ratio.Height <= 0)
            return 16.0 / 9.0;
        return (double)ratio.Width / ratio.Height;
    }

    // ---- Threads -------------------------------------------------------------------------

    /// <summary>
    /// Flattens a thread into rows: every ancestor (oldest first), the opened post, then its
    /// replies depth-first up to <paramref name="maxDepth"/> levels. Deleted and blocked posts
    /// in the chain are skipped rather than shown as placeholders.
    /// </summary>
    public static List<ThreadRow> FlattenThread(PostThread thread, int maxDepth = 3)
    {
        List<ThreadRow> rows = [];

        if (thread.Thread is not ThreadViewPost main || main.Post is null)
            return rows;

        List<PostView> ancestors = AncestorsOf(main);

        string? previousHandle = null;
        foreach (PostView ancestor in ancestors)
        {
            rows.Add(new ThreadRow { Post = ToPostItem(ancestor, replyingTo: previousHandle), Depth = 0 });
            previousHandle = ancestor.Author?.Handle?.ToString();
        }

        rows.Add(new ThreadRow { Post = ToPostItem(main.Post, replyingTo: previousHandle), Depth = 0, IsMain = true });

        AddReplies(rows, main, main.Post.Author?.Handle?.ToString(), depth: 1, maxDepth);
        return rows;
    }

    /// <summary>The posts above a thread's main post, oldest first; stops at the first one that is gone.</summary>
    public static List<PostView> AncestorsOf(ThreadViewPost main)
    {
        List<PostView> ancestors = [];
        PostViewBase? cursor = main.Parent;
        while (cursor is ThreadViewPost parent && parent.Post is not null)
        {
            ancestors.Insert(0, parent.Post);
            cursor = parent.Parent;
        }
        return ancestors;
    }

    private static void AddReplies(List<ThreadRow> rows, ThreadViewPost parent, string? parentHandle, int depth, int maxDepth)
    {
        if (parent.Replies is null || depth > maxDepth)
            return;

        foreach (PostViewBase replyBase in parent.Replies)
        {
            if (replyBase is not ThreadViewPost reply || reply.Post is null)
                continue;

            rows.Add(new ThreadRow { Post = ToPostItem(reply.Post, replyingTo: parentHandle), Depth = depth });
            AddReplies(rows, reply, reply.Post.Author?.Handle?.ToString(), depth + 1, maxDepth);
        }
    }

    // ---- Notifications -------------------------------------------------------------------

    public static NotificationKind ToKind(NotificationReason reason) => reason switch
    {
        NotificationReason.Follow => NotificationKind.Follow,
        NotificationReason.Like or NotificationReason.LikeViaRepost => NotificationKind.Like,
        NotificationReason.Repost or NotificationReason.RepostViaRepost => NotificationKind.Repost,
        NotificationReason.Mention => NotificationKind.Mention,
        NotificationReason.Reply => NotificationKind.Reply,
        NotificationReason.Quote => NotificationKind.Quote,
        NotificationReason.StarterPackJoined => NotificationKind.StarterPackJoined,
        NotificationReason.Verified => NotificationKind.Verified,
        NotificationReason.SubscribedPost => NotificationKind.SubscribedPost,
        _ => NotificationKind.Other
    };

    public static NotificationInput ToInput(Notification notification) => new(
        Id: notification.Uri.ToString(),
        Kind: ToKind(notification.Reason),
        AuthorDisplayName: DisplayNameOf(notification.Author),
        SubjectUri: SubjectUriOf(notification),
        IsRead: notification.IsRead,
        IndexedAt: notification.IndexedAt);

    /// <summary>
    /// The AT URI of the post a like/repost notification is about. <c>ReasonSubject</c> is
    /// meant to carry this but comes back null for some notifications in practice, so this
    /// falls back to the like/repost record's own subject reference.
    /// </summary>
    public static string? SubjectUriOf(Notification notification)
    {
        if (notification.ReasonSubject is not null)
            return notification.ReasonSubject.ToString();

        return notification.Record switch
        {
            Like like => like.Subject?.Uri?.ToString(),
            Repost repost => repost.Subject?.Uri?.ToString(),
            _ => null
        };
    }

    /// <summary>
    /// Builds a row from a group plus the raw notifications it came from (keyed by URI) and any
    /// hydrated posts (keyed by AT URI) fetched for the subjects.
    /// </summary>
    public static NotificationItem ToNotificationItem(
        NotificationGroup group,
        IReadOnlyDictionary<string, Notification> raw,
        IReadOnlyDictionary<string, PostView> hydratedPosts)
    {
        Notification newest = raw[group.Newest.Id];
        ProfileViewBasic author = newest.Author;
        string handle = author.Handle?.ToString() ?? string.Empty;

        string? bodyText = null;
        string? subjectAtUri = null;
        string? webUrl = null;
        PostItem? post = null;
        PostItem? subjectPost = null;

        switch (group.Kind)
        {
            case NotificationKind.Mention:
            case NotificationKind.Reply:
            case NotificationKind.Quote:
            case NotificationKind.SubscribedPost:
                // The notification *is* their post; its record has the text, and the hydrated
                // view (if fetched) has counts and viewer state for the action buttons.
                subjectAtUri = newest.Uri.ToString();
                if (hydratedPosts.TryGetValue(subjectAtUri, out PostView? theirs))
                    post = ToPostItem(theirs);
                bodyText = post?.Text ?? (newest.Record as Post)?.Text;
                webUrl = BlueskyLinks.PostUrl(subjectAtUri, handle);
                subjectPost = post;
                break;

            case NotificationKind.Like:
            case NotificationKind.Repost:
                subjectAtUri = group.SubjectUri;
                if (subjectAtUri is not null && hydratedPosts.TryGetValue(subjectAtUri, out PostView? mine))
                {
                    bodyText = mine.Record?.Text;
                    webUrl = BlueskyLinks.PostUrl(subjectAtUri, mine.Author?.Handle?.ToString());
                    subjectPost = ToPostItem(mine);
                }
                break;

            case NotificationKind.Follow:
                webUrl = BlueskyLinks.ProfileUrl(handle.Length > 0 ? handle : author.Did.ToString());
                break;
        }

        return new NotificationItem
        {
            Kind = group.Kind,
            Headline = NotificationGroupingPolicy.Headline(group),
            AvatarUri = author.Avatar,
            AuthorHandle = handle,
            AuthorDisplayName = DisplayNameOf(author),
            AuthorDid = author.Did.ToString(),
            BodyText = bodyText,
            IndexedAt = group.Newest.IndexedAt,
            IsUnread = group.IsUnread,
            SubjectAtUri = subjectAtUri,
            WebUrl = webUrl,
            Post = post,
            SubjectPost = subjectPost
        };
    }
}
