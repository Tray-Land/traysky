using System;
using System.Collections.Generic;
using System.Linq;
using idunno.AtProto;
using idunno.AtProto.Repo;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Debug-only sample data so the timeline, notifications and compose UI can be checked
/// without an account or network. Set <c>TRAYSKY_PREVIEW=1</c> in the environment before
/// launching a Debug build, or - since a packaged app launched from the shell inherits no
/// environment - drop an empty <c>preview.flag</c> file in the package's LocalState folder.
/// Never compiled into Release.
/// </summary>
public static class PreviewMode
{
#if DEBUG
    public static bool IsEnabled { get; } = Detect();

    private static bool Detect()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TRAYSKY_PREVIEW"), "1", StringComparison.Ordinal))
            return true;

        try
        {
            return System.IO.File.Exists(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "preview.flag"));
        }
        catch
        {
            return false;
        }
    }
#else
    public static bool IsEnabled => false;
#endif

    public const string Handle = "preview.bsky.social";

    private static StrongReference FakeRef(int n) =>
        new(new AtUri($"at://did:plc:preview{n}/app.bsky.feed.post/3k{n:d6}"), new Cid("bafyreigdyrzt5sfp7udm7hu76uh7y26nf3efuylqabf3oclgtqy55fbzdi"));

    public static List<PostItem> SamplePosts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string t1 = "Shipping a tiny Bluesky client that lives in the Windows tray 🦋 Built on idunno.Bluesky and the bones of Traydio. #WinUI #dotnet";
        string t2 = "Handles do not start with an @ sign, that's just how the official app displays them. Read the docs: https://bluesky.idunno.dev/";
        string t3 = "Reply-with-a-picture test. Alt text matters.";
        string t4 = "Quoting this because the point stands.";

        return
        [
            new PostItem
            {
                AtUri = "at://did:plc:preview1/app.bsky.feed.post/3k000001", Cid = "bafy1", Reference = FakeRef(1),
                AuthorDid = "did:plc:preview1", AuthorHandle = "joe.bsky.social", AuthorDisplayName = "Joe Finney",
                Text = t1,
                Segments = FacetSegmenter.Segment(t1,
                [
                    new FacetInput(Utf8Index(t1, "#WinUI"), Utf8Index(t1, "#WinUI") + 6, FacetKind.Tag, "WinUI"),
                    new FacetInput(Utf8Index(t1, "#dotnet"), Utf8Index(t1, "#dotnet") + 7, FacetKind.Tag, "dotnet")
                ]),
                CreatedAt = now.AddMinutes(-4), LikeCount = 12, RepostCount = 3, ReplyCount = 2,
                WebUrl = "https://bsky.app/profile/joe.bsky.social/post/3k000001"
            },
            new PostItem
            {
                AtUri = "at://did:plc:preview2/app.bsky.feed.post/3k000002", Cid = "bafy2", Reference = FakeRef(2),
                AuthorDid = "did:plc:preview2", AuthorHandle = "blowdart.bsky.social", AuthorDisplayName = "Barry Dorrans",
                AuthorLabels =
                [
                    new LabelDisplay("sexual", "Sexually suggestive", "Does not include nudity.", LabelSeverity.Alert, "Bluesky Moderation Service", false),
                    new LabelDisplay("dotnet-dev", ".NET developer", "Writes software on the .NET platform.", LabelSeverity.Inform, "Community Labeler", false)
                ],
                Text = t2,
                Segments = FacetSegmenter.Segment(t2,
                [
                    new FacetInput(Utf8Index(t2, "https://"), Utf8Index(t2, "https://") + "https://bluesky.idunno.dev/".Length, FacetKind.Link, "https://bluesky.idunno.dev/")
                ]),
                CreatedAt = now.AddHours(-2), LikeCount = 48, RepostCount = 9, ReplyCount = 5, IsLiked = true, LikeUri = "at://x/app.bsky.feed.like/1",
                RepostedBy = "Someone You Follow",
                Embed = new EmbedItem
                {
                    Kind = EmbedKind.External,
                    ExternalUri = new Uri("https://bluesky.idunno.dev/"),
                    ExternalHost = "bluesky.idunno.dev",
                    ExternalTitle = "idunno.Bluesky — a .NET library for Bluesky",
                    ExternalDescription = "Getting started, connecting, posting, timelines, notifications and more.",
                    OpenUrl = "https://bluesky.idunno.dev/"
                },
                WebUrl = "https://bsky.app/profile/blowdart.bsky.social/post/3k000002"
            },
            new PostItem
            {
                AtUri = "at://did:plc:preview3/app.bsky.feed.post/3k000003", Cid = "bafy3", Reference = FakeRef(3),
                AuthorDid = "did:plc:preview3", AuthorHandle = "photos.bsky.social", AuthorDisplayName = "Photo Person",
                Text = t3, Segments = FacetSegmenter.Segment(t3, null),
                CreatedAt = now.AddDays(-1), LikeCount = 3, ReplyingTo = "joe.bsky.social",
                Embed = new EmbedItem
                {
                    Kind = EmbedKind.Images,
                    Images =
                    [
                        new EmbedImage(new Uri("ms-appx:///Assets/Wide310x150Logo.scale-200.png"), new Uri("ms-appx:///Assets/Wide310x150Logo.scale-200.png"), "Wide logo", 620.0 / 300),
                        new EmbedImage(new Uri("ms-appx:///Assets/Square150x150Logo.scale-200.png"), new Uri("ms-appx:///Assets/Square150x150Logo.scale-200.png"), "Square logo", 1)
                    ]
                },
                WebUrl = "https://bsky.app/profile/photos.bsky.social/post/3k000003"
            },
            new PostItem
            {
                AtUri = "at://did:plc:preview4/app.bsky.feed.post/3k000004", Cid = "bafy4", Reference = FakeRef(4),
                AuthorDid = "did:plc:preview4", AuthorHandle = "quoter.bsky.social", AuthorDisplayName = "Quote Person",
                Text = t4, Segments = FacetSegmenter.Segment(t4, null),
                CreatedAt = now.AddDays(-9), LikeCount = 0, IsReposted = true, RepostUri = "at://x/app.bsky.feed.repost/1", RepostCount = 1,
                Embed = new EmbedItem
                {
                    Kind = EmbedKind.Quote,
                    QuoteAuthorName = "Joe Finney",
                    QuoteAuthorHandle = "joe.bsky.social",
                    QuoteText = t1,
                    QuoteWebUrl = "https://bsky.app/profile/joe.bsky.social/post/3k000001",
                    QuoteAtUri = "at://did:plc:preview1/app.bsky.feed.post/3k000001",
                    OpenUrl = "https://bsky.app/profile/joe.bsky.social/post/3k000001"
                },
                WebUrl = "https://bsky.app/profile/quoter.bsky.social/post/3k000004"
            }
        ];
    }

    /// <summary>
    /// The sample posts as timeline rows, with two sample replies: one to a single post and one
    /// deep in a thread (so the connector lines and the "Load more" row show up).
    /// </summary>
    public static List<object> SampleTimelineRows(bool includeContext)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PostItem Make(int n, string handle, string name, string text, DateTimeOffset at, bool lineAbove = false, bool lineBelow = false) => new()
        {
            AtUri = $"at://did:plc:preview{n}/app.bsky.feed.post/3k{n:d6}", Cid = $"bafy{n}", Reference = FakeRef(n),
            AuthorDid = $"did:plc:preview{n}", AuthorHandle = handle, AuthorDisplayName = name,
            Text = text, Segments = FacetSegmenter.Segment(text, null),
            CreatedAt = at, LikeCount = n % 7, ReplyCount = n % 3,
            // Context posts never carry a "Replying to" label (see FeedMapper.ContextPost).
            ReplyingTo = lineBelow ? string.Empty : "joe.bsky.social",
            ThreadLineAbove = lineAbove, ThreadLineBelow = lineBelow,
            WebUrl = $"https://bsky.app/profile/{handle}/post/3k{n:d6}"
        };

        List<PostItem> posts = SamplePosts();
        List<object> rows = [];

        if (includeContext)
        {
            // A reply straight to someone's post.
            rows.Add(Make(11, "joe.bsky.social", "Joe Finney", "Anyone know a good way to test a WinUI tray flyout?", now.AddMinutes(-9), lineBelow: true));
            rows.Add(Make(12, "blowdart.bsky.social", "Barry Dorrans", "Drive it with a preview flag and sample data, then trust nothing.", now.AddMinutes(-6), lineAbove: true));

            // A reply deep in a thread: first post, Load more, the post replied to, the reply.
            PostItem deepReply = Make(15, "photos.bsky.social", "Photo Person", "Same here, and it only reproduces on ARM.", now.AddMinutes(-3), lineAbove: true);
            rows.Add(Make(13, "joe.bsky.social", "Joe Finney", "Shipped 1.3.0 today. The flyout is much snappier.", now.AddHours(-1), lineBelow: true));
            rows.Add(new ThreadGapItem { Reply = deepReply, RootUri = "at://did:plc:preview13/app.bsky.feed.post/3k000013", ParentUri = "at://did:plc:preview14/app.bsky.feed.post/3k000014" });
            rows.Add(Make(14, "blowdart.bsky.social", "Barry Dorrans", "Nice. Did the idle memory number come down too?", now.AddMinutes(-20), lineAbove: true, lineBelow: true));
            rows.Add(deepReply);
        }

        rows.AddRange(posts);
        return rows;
    }

    public static List<NotificationItem> SampleNotifications()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<PostItem> posts = SamplePosts();

        return
        [
            new NotificationItem
            {
                Kind = NotificationKind.Reply, Headline = "Photo Person replied to you", AuthorHandle = "photos.bsky.social", AuthorDisplayName = "Photo Person", AuthorDid = "did:plc:preview3",
                BodyText = posts[2].Text, IndexedAt = now.AddMinutes(-10), IsUnread = true, Post = posts[2], SubjectPost = posts[2], WebUrl = posts[2].WebUrl
            },
            new NotificationItem
            {
                Kind = NotificationKind.Like, Headline = "Barry Dorrans, Photo Person and 3 others liked your post", AuthorHandle = "blowdart.bsky.social", AuthorDisplayName = "Barry Dorrans", AuthorDid = "did:plc:preview2",
                BodyText = posts[0].Text, IndexedAt = now.AddHours(-1), IsUnread = true, SubjectPost = posts[0], WebUrl = posts[0].WebUrl
            },
            new NotificationItem
            {
                Kind = NotificationKind.Follow, Headline = "Quote Person and Someone Else followed you", AuthorHandle = "quoter.bsky.social", AuthorDisplayName = "Quote Person", AuthorDid = "did:plc:preview4",
                IndexedAt = now.AddHours(-5), IsUnread = false, WebUrl = "https://bsky.app/profile/quoter.bsky.social"
            },
            new NotificationItem
            {
                Kind = NotificationKind.Repost, Headline = "Quote Person reposted your post", AuthorHandle = "quoter.bsky.social", AuthorDisplayName = "Quote Person", AuthorDid = "did:plc:preview4",
                BodyText = posts[0].Text, IndexedAt = now.AddDays(-2), IsUnread = false, SubjectPost = posts[0], WebUrl = posts[0].WebUrl
            }
        ];
    }

    /// <summary>A handful of pinned feeds so the tab row has something to show without a network call.</summary>
    public static List<FeedTabItem> SampleFeedTabs() =>
    [
        new FeedTabItem { Kind = FeedTabKind.Timeline, DisplayName = "Following" },
        new FeedTabItem
        {
            Kind = FeedTabKind.Feed,
            DisplayName = "Discover",
            Uri = new AtUri("at://did:plc:preview-discover/app.bsky.feed.generator/discover")
        },
        new FeedTabItem
        {
            Kind = FeedTabKind.Feed,
            DisplayName = "News",
            Uri = new AtUri("at://did:plc:preview-news/app.bsky.feed.generator/news")
        }
    ];

    /// <summary>Accounts for the Blocked / Muted screen.</summary>
    public static List<ActorItem> SampleManagedAccounts(bool blocks)
    {
        string label = ManagedAccountsPolicy.ActionLabel(blocks);
        string? Uri(int n) => blocks ? $"at://did:plc:preview-self/app.bsky.graph.block/3kb00{n}" : null;
        return
        [
            new ActorItem { Did = "did:plc:preview-m1", Handle = "spammer.bsky.social", DisplayName = "Totally Real Giveaways", BlockUri = Uri(1), ManagedActionLabel = label },
            new ActorItem { Did = "did:plc:preview-m2", Handle = "argue.bsky.social", DisplayName = "Always Arguing", BlockUri = Uri(2), ManagedActionLabel = label },
            new ActorItem { Did = "did:plc:preview-m3", Handle = "quiet.example.com", BlockUri = Uri(3), ManagedActionLabel = label }
        ];
    }

    /// <summary>Accounts whose bell is on, for the notification-bell list.</summary>
    public static List<ActorItem> SampleActivitySubscriptions() =>
    [
        new ActorItem { Did = "did:plc:preview1", Handle = "joe.bsky.social", DisplayName = "Joe Finney", ManagedActionLabel = "Turn off", ManagedDetail = "Posts and replies" },
        new ActorItem { Did = "did:plc:preview-s3", Handle = "dotnet.example.com", DisplayName = ".NET Community", ManagedActionLabel = "Turn off", ManagedDetail = "Posts" }
    ];

    /// <summary>Accounts for the search page's People tab and a starter pack's members.</summary>
    public static List<ActorItem> SampleSearchActors() =>
    [
        new ActorItem { Did = "did:plc:preview1", Handle = "joe.bsky.social", DisplayName = "Joe Finney" },
        new ActorItem { Did = "did:plc:preview2", Handle = "blowdart.bsky.social", DisplayName = "Barry Dorrans" },
        new ActorItem { Did = "did:plc:preview-s3", Handle = "dotnet.example.com", DisplayName = ".NET Community" }
    ];

    /// <summary>Starter packs for the search page's Starter packs tab.</summary>
    public static List<StarterPackItem> SampleStarterPacks() =>
    [
        new StarterPackItem
        {
            Name = ".NET developers", Description = "People posting about C#, F# and the .NET platform.",
            CreatorHandle = "joe.bsky.social", MemberCount = 42, JoinedCount = 310,
            AtUri = "at://did:plc:preview1/app.bsky.graph.starterpack/3kp001",
            ListUri = "at://did:plc:preview1/app.bsky.graph.list/3kl001",
            WebUrl = "https://bsky.app/starter-pack/joe.bsky.social/3kp001"
        },
        new StarterPackItem
        {
            Name = "Windows desktop", CreatorHandle = "blowdart.bsky.social", MemberCount = 17,
            AtUri = "at://did:plc:preview2/app.bsky.graph.starterpack/3kp002",
            ListUri = "at://did:plc:preview2/app.bsky.graph.list/3kl002",
            WebUrl = "https://bsky.app/starter-pack/blowdart.bsky.social/3kp002"
        }
    ];

    /// <summary>Lists for a profile's Lists tab, owned by the preview account so they can be edited.</summary>
    public static List<ListItem> SampleLists() =>
    [
        new ListItem
        {
            Uri = "at://did:plc:preview-self/app.bsky.graph.list/3kl101", Name = "Windows devs",
            Description = "People building for Windows.", Kind = ListKind.Curate, MemberCount = 3,
            CreatorDid = SelfDid, CreatorHandle = Handle
        },
        new ListItem
        {
            Uri = "at://did:plc:preview-self/app.bsky.graph.list/3kl102", Name = "Mute: engagement bait",
            Kind = ListKind.Moderation, MemberCount = 1,
            CreatorDid = SelfDid, CreatorHandle = Handle
        },
        new ListItem
        {
            Uri = "at://did:plc:preview1/app.bsky.graph.list/3kl103", Name = "Spam accounts",
            Description = "Shared block list of known spam.", Kind = ListKind.Moderation, MemberCount = 240,
            CreatorDid = "did:plc:preview1", CreatorHandle = "alice.example.com", IsMuted = true
        }
    ];

    /// <summary>Checkbox rows for the "Add to list" dialog.</summary>
    public static List<ListMembershipItem> SampleMemberships()
    {
        List<ListItem> lists = SampleLists();
        return [new ListMembershipItem(lists[0], "at://did:plc:preview1/app.bsky.graph.listitem/3kli01"), new ListMembershipItem(lists[1], null)];
    }

    /// <summary>Saved drafts for the compose box's Drafts flyout: a plain one, one with reply limits, and one written elsewhere with media.</summary>
    public static List<DraftItem> SampleDrafts() =>
    [
        new DraftItem { Text = "Thinking about a tray app for tracking release notes, anyone else want one?", Age = "12m" },
        new DraftItem
        {
            Text = "Small announcement coming tomorrow.\n\nStay tuned.",
            Reply = new ReplyChoice(ReplyAudience.Limited, Followers: true),
            AllowQuotes = false,
            Age = "2h"
        },
        new DraftItem
        {
            Text = "Sunset from the ferry tonight",
            Age = "3d",
            Note = DraftPolicy.RestoreNote(2, 0)
        }
    ];

    /// <summary>Feeds for a profile's Feeds tab.</summary>
    public static List<FeedSuggestionItem> SampleFeedSuggestions() =>
    [
        new FeedSuggestionItem
        {
            AtUri = DotNetNewsFeed, IsPinned = true,
            DisplayName = ".NET News", Description = "Release notes, blog posts and community links for .NET.",
            CreatorHandle = "joe.bsky.social", LikeCount = 1280,
            WebUrl = "https://bsky.app/profile/did:plc:preview1/feed/dotnet-news"
        },
        new FeedSuggestionItem
        {
            AtUri = DesktopDevsFeed,
            DisplayName = "Desktop devs", CreatorHandle = "blowdart.bsky.social", LikeCount = 96,
            WebUrl = "https://bsky.app/profile/did:plc:preview2/feed/desktop-devs"
        }
    ];

    private const string DotNetNewsFeed = "at://did:plc:preview1/app.bsky.feed.generator/dotnet-news";
    private const string DesktopDevsFeed = "at://did:plc:preview2/app.bsky.feed.generator/desktop-devs";

    /// <summary>The saved-feeds list behind "Your feeds": Following and one feed pinned, one feed saved but not pinned.</summary>
    public static List<SavedFeedEntry> SampleSavedFeeds() =>
    [
        new("pv-1", SavedFeedPolicy.TimelineType, "following", true),
        new("pv-2", SavedFeedPolicy.FeedType, DotNetNewsFeed, true),
        new("pv-3", SavedFeedPolicy.FeedType, DesktopDevsFeed, false)
    ];

    /// <summary>Names for <see cref="SampleSavedFeeds"/>, keyed by entry value.</summary>
    public static Dictionary<string, SavedFeedLabel> SampleSavedFeedLabels() => new()
    {
        [DotNetNewsFeed] = new SavedFeedLabel(".NET News", null),
        [DesktopDevsFeed] = new SavedFeedLabel("Desktop devs", null)
    };

    /// <summary>The preview account's profile as the editor first shows it.</summary>
    public static (string DisplayName, string Bio) SampleProfileText() =>
        ("Joe Finney", "Building small Windows tray apps. Opinions are my own and mostly about XAML.");

    /// <summary>The preview account's DID, so its sample messages line up on the "mine" side.</summary>
    public const string SelfDid = "did:plc:preview-self";

    /// <summary>A few conversations for the Messages page: one unread, one request, one quiet.</summary>
    public static List<ConversationItem> SampleConversations()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return
        [
            new ConversationItem
            {
                Id = "preview-convo-1", Title = "Joe Finney", Subtitle = "@joe.bsky.social", OtherProfileKey = "joe.bsky.social",
                LastMessagePreview = "Does the tray icon badge work for you?", LastMessageAt = now.AddMinutes(-3), UnreadCount = 2
            },
            new ConversationItem
            {
                Id = "preview-convo-2", Title = "Photo Person", Subtitle = "@photos.bsky.social", OtherProfileKey = "photos.bsky.social",
                LastMessagePreview = "Hi! Loved your latest post about WinUI.", LastMessageAt = now.AddHours(-2), UnreadCount = 1, IsRequest = true
            },
            new ConversationItem
            {
                Id = "preview-convo-3", Title = "Barry Dorrans", Subtitle = "@blowdart.bsky.social", OtherProfileKey = "blowdart.bsky.social",
                LastMessagePreview = "You: Thanks, will do", LastMessageAt = now.AddDays(-3)
            },
            SampleNewGroup("Tray app club", 3)
        ];
    }

    /// <summary>The preview account's own group, as it appears in the Messages list and when one is created.</summary>
    public static ConversationItem SampleNewGroup(string name, int others)
    {
        Dictionary<string, ActorItem> members = SampleGroupMembers().ToDictionary(m => m.Actor.Did, m => m.Actor, StringComparer.Ordinal);
        long count = others + 1;
        return new ConversationItem
        {
            Id = "preview-group-1",
            Title = name,
            Subtitle = GroupChatPolicy.MemberCountText(count, 100),
            IsGroup = true,
            IsOwner = true,
            MemberCount = count,
            MemberLimit = 100,
            Members = members,
            LastMessagePreview = "Joe Finney: Anyone tried the new group chats?",
            LastMessageAt = DateTimeOffset.UtcNow.AddMinutes(-12),
            UnreadCount = 0
        };
    }

    /// <summary>Who is in the sample group: the preview account (owner) and two others.</summary>
    public static List<GroupMember> SampleGroupMembers() =>
    [
        new GroupMember(new ActorItem { Did = SelfDid, Handle = "preview.bsky.social", DisplayName = "Preview Account" }, true),
        new GroupMember(new ActorItem { Did = "did:plc:preview-joe", Handle = "joe.bsky.social", DisplayName = "Joe Finney" }, false),
        new GroupMember(new ActorItem { Did = "did:plc:preview-barry", Handle = "blowdart.bsky.social", DisplayName = "Barry Dorrans" }, false)
    ];

    /// <summary>What a pasted join link shows, so the join dialog can be seen without an account.</summary>
    public static GroupJoinPreview SampleJoinPreview(string code) =>
        new(code, "Tray app club", "Joe Finney", 3, 100, RequireApproval: true, FollowedByOwnerOnly: false,
            IsDisabled: false, IsInvalid: false, RequestPending: false, AlreadyIn: null);

    private static List<MessageItem> SampleGroupMessages(ConversationItem group)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        MessageItem Line(int n, string text, DateTimeOffset at) => new()
        {
            Id = $"{group.Id}-{n}", IsSystem = true, IsInGroup = true, Text = text,
            Segments = FacetSegmenter.Segment(text, null), SentAt = at
        };

        MessageItem Msg(int n, string did, string who, string text, DateTimeOffset at) => new()
        {
            Id = $"{group.Id}-{n}", SenderDid = did, SenderName = who, IsMine = did == SelfDid, IsInGroup = true,
            Text = text, Segments = FacetSegmenter.Segment(text, null), SentAt = at
        };

        return
        [
            Line(1, GroupChatPolicy.EventText(GroupEvent.MemberAdded, "You", "Joe Finney"), now.AddHours(-3)),
            Line(2, GroupChatPolicy.EventText(GroupEvent.MemberAdded, "You", "Barry Dorrans"), now.AddHours(-3)),
            Msg(3, SelfDid, "Preview Account", "Welcome both. This is where we swap tray app ideas.", now.AddHours(-2)),
            Msg(4, "did:plc:preview-barry", "Barry Dorrans", "Nice. I have opinions about flyouts.", now.AddMinutes(-40)),
            Line(5, GroupChatPolicy.EventText(GroupEvent.MemberJoined, string.Empty, "Alice Example"), now.AddMinutes(-20)),
            Msg(6, "did:plc:preview-joe", "Joe Finney", "Anyone tried the new group chats?", now.AddMinutes(-12))
        ];
    }

    public static List<MessageItem> SampleMessages(ConversationItem conversation)
    {
        if (conversation.IsGroup)
            return SampleGroupMessages(conversation);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string them = "did:plc:" + conversation.Id;

        MessageItem Msg(int n, bool mine, string text, DateTimeOffset at, string reactions = "") => new()
        {
            Id = $"{conversation.Id}-{n}",
            SenderDid = mine ? SelfDid : them,
            SenderName = mine ? "Preview Account" : conversation.Title,
            IsMine = mine,
            Text = text,
            Segments = FacetSegmenter.Segment(text, null),
            ReactionsText = reactions,
            SentAt = at
        };

        return conversation.Id switch
        {
            "preview-convo-1" =>
            [
                Msg(1, true, "Pushed a build with DMs in it 🦋", now.AddHours(-26)),
                Msg(2, false, "Nice, grabbing it now", now.AddMinutes(-20), "👍"),
                Msg(3, false, "Messages open from the title bar?", now.AddMinutes(-19)),
                Msg(4, true, "Yep, next to the bell. Only shows if your app password has DM access.", now.AddMinutes(-10)),
                Msg(5, false, "Does the tray icon badge work for you?", now.AddMinutes(-3)),
            ],
            "preview-convo-2" =>
            [
                Msg(1, false, "Hi! Loved your latest post about WinUI.", now.AddHours(-2)),
            ],
            _ =>
            [
                Msg(1, false, "Can you file that as an issue so I don't forget?", now.AddDays(-3).AddMinutes(-5)),
                Msg(2, true, "Thanks, will do", now.AddDays(-3), "❤️"),
            ]
        };
    }

    private static long Utf8Index(string text, string needle) =>
        System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, text.IndexOf(needle, StringComparison.Ordinal)));
}
