using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class SavedThreadTests
{
    private static SavedThread Sample() => new()
    {
        Posts =
        [
            new SavedPost
            {
                Text = "First post",
                Label = nameof(ComposeContentLabel.Nudity),
                Media = [new SavedMedia { FileName = "a.png", MimeType = "image/png", AltText = "alt", PixelWidth = 4, PixelHeight = 3, StoredAs = "p0m0.bin", Bytes = [1, 2, 3] }]
            },
            new SavedPost { Text = "Second" },
            new SavedPost { Text = "Third", Media = [new SavedMedia { FileName = "v.mp4", MimeType = "video/mp4", IsVideo = true }] }
        ],
        Langs = ["en", "es"],
        QuoteUri = "at://did:plc:abc/app.bsky.feed.post/xyz",
        AllowQuotes = false,
        ScheduledAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero)
    };

    [TestMethod]
    public void Json_RoundTripsEverythingExceptBytes()
    {
        SavedThread original = Sample();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(original, SavedThreadJsonContext.Default.SavedThread);
        SavedThread back = JsonSerializer.Deserialize(json, SavedThreadJsonContext.Default.SavedThread)!;

        Assert.AreEqual(original.Id, back.Id);
        Assert.AreEqual(3, back.Posts.Count);
        Assert.AreEqual("First post", back.Posts[0].Text);
        Assert.AreEqual("alt", back.Posts[0].Media[0].AltText);
        Assert.AreEqual("p0m0.bin", back.Posts[0].Media[0].StoredAs);
        Assert.IsNull(back.Posts[0].Media[0].Bytes, "bytes live in their own files, never in the JSON");
        Assert.IsTrue(back.Posts[2].Media[0].IsVideo);
        CollectionAssert.AreEqual(new[] { "en", "es" }, back.Langs);
        Assert.AreEqual(original.QuoteUri, back.QuoteUri);
        Assert.IsFalse(back.AllowQuotes);
        Assert.AreEqual(original.ScheduledAt, back.ScheduledAt);
    }

    [TestMethod]
    public void ReplyControls_RoundTrip()
    {
        var thread = new SavedThread();
        SavedThreadPolicy.SetReply(thread, new ReplyChoice(ReplyAudience.Limited, Mentioned: true, Following: true, ListUris: ["at://list/1"]));

        ReplyChoice back = SavedThreadPolicy.ReplyOf(thread);
        Assert.AreEqual(ReplyAudience.Limited, back.Audience);
        Assert.IsTrue(back.Mentioned);
        Assert.IsFalse(back.Followers);
        Assert.IsTrue(back.Following);
        CollectionAssert.AreEqual(new[] { "at://list/1" }, back.Lists.ToArray());
    }

    [TestMethod]
    public void ReplyOf_UnknownAudienceFallsBackToEveryone()
    {
        Assert.AreEqual(ReplyAudience.Everyone, SavedThreadPolicy.ReplyOf(new SavedThread { ReplyAudience = "Bogus" }).Audience);
    }

    [TestMethod]
    public void HasContent_NeedsTextMediaOrQuote()
    {
        Assert.IsFalse(SavedThreadPolicy.HasContent(new SavedThread { Posts = [new SavedPost { Text = "  " }] }));
        Assert.IsTrue(SavedThreadPolicy.HasContent(new SavedThread { Posts = [new SavedPost { Text = "hi" }] }));
        Assert.IsTrue(SavedThreadPolicy.HasContent(new SavedThread { Posts = [new SavedPost { Media = [new SavedMedia()] }] }));
        Assert.IsTrue(SavedThreadPolicy.HasContent(new SavedThread { QuoteUri = "at://x", Posts = [new SavedPost()] }));
    }

    [TestMethod]
    public void Summary_DescribesPostsAndMedia()
    {
        Assert.AreEqual("3 posts · 1 image · 1 video", SavedThreadPolicy.Summary(Sample()));
        Assert.IsNull(SavedThreadPolicy.Summary(new SavedThread { Posts = [new SavedPost { Text = "hi" }] }));
    }

    [TestMethod]
    public void Preview_UsesFirstPostAndNamesMediaOnlyDrafts()
    {
        Assert.AreEqual("First post", SavedThreadPolicy.Preview(Sample()));
        Assert.AreEqual("(media only)", SavedThreadPolicy.Preview(new SavedThread { Posts = [new SavedPost { Media = [new SavedMedia()] }] }));
    }

    [TestMethod]
    public void State_FollowsTheSchedule()
    {
        var now = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(SavedThreadState.Draft, SavedThreadPolicy.StateAt(new SavedThread(), now));
        Assert.AreEqual(SavedThreadState.Scheduled, SavedThreadPolicy.StateAt(new SavedThread { ScheduledAt = now.AddHours(1) }, now));
        Assert.AreEqual(SavedThreadState.Overdue, SavedThreadPolicy.StateAt(new SavedThread { ScheduledAt = now.AddHours(-1) }, now));
    }

    [TestMethod]
    public void Overdue_ListsOnlyPastDueSoonestFirst()
    {
        var now = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new SavedThread { ScheduledAt = now.AddHours(-1) };
        var later = new SavedThread { ScheduledAt = now.AddHours(-5) };
        var future = new SavedThread { ScheduledAt = now.AddHours(2) };
        var draft = new SavedThread();

        IReadOnlyList<SavedThread> overdue = SavedThreadPolicy.Overdue([late, future, draft, later], now);
        CollectionAssert.AreEqual(new[] { later, late }, overdue.ToArray());
    }

    [TestMethod]
    public void MirrorNote_SaysWhatBlueskyDraftsLeaveOut()
    {
        Assert.IsNull(SavedThreadPolicy.MirrorNote(false, false));
        StringAssert.Contains(SavedThreadPolicy.MirrorNote(true, false), "kept on this PC");
        StringAssert.Contains(SavedThreadPolicy.MirrorNote(false, true), "replying to");
    }

    [TestMethod]
    public void MediaFileName_IsStablePerPosition()
    {
        Assert.AreEqual("p2m1.bin", SavedThreadPolicy.MediaFileName(2, 1));
    }

    [TestMethod]
    public void LabelOf_ParsesNamesAndIgnoresUnknown()
    {
        Assert.AreEqual(ComposeContentLabel.Graphic, SavedThreadPolicy.LabelOf(new SavedPost { Label = "Graphic" }));
        Assert.AreEqual(ComposeContentLabel.None, SavedThreadPolicy.LabelOf(new SavedPost { Label = "???" }));
    }
}
