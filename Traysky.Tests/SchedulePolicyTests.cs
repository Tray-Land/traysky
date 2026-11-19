using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class SchedulePolicyTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static SavedThread Scheduled(TimeSpan fromNow, int attempts = 0, TimeSpan? lastAttemptAgo = null) => new()
    {
        ScheduledAt = Now + fromNow,
        Attempts = attempts,
        LastAttemptAt = lastAttemptAgo is { } ago ? Now - ago : null,
        Posts = [new SavedPost { Text = "Hello" }]
    };

    [TestMethod]
    public void DueAt_WaitsForTheTimeThenPosts()
    {
        Assert.AreEqual(ScheduleDue.Wait, SchedulePolicy.DueAt(new SavedThread(), Now), "a plain draft is never due");
        Assert.AreEqual(ScheduleDue.Wait, SchedulePolicy.DueAt(Scheduled(TimeSpan.FromMinutes(1)), Now));
        Assert.AreEqual(ScheduleDue.Post, SchedulePolicy.DueAt(Scheduled(TimeSpan.Zero), Now));
        Assert.AreEqual(ScheduleDue.Post, SchedulePolicy.DueAt(Scheduled(-TimeSpan.FromMinutes(10)), Now), "a few minutes late still goes out");
    }

    [TestMethod]
    public void DueAt_PastTheGraceIsMissedNotPosted()
    {
        Assert.AreEqual(ScheduleDue.Missed, SchedulePolicy.DueAt(Scheduled(-(SchedulePolicy.LateGrace + TimeSpan.FromSeconds(1))), Now));
        Assert.AreEqual(ScheduleDue.Missed, SchedulePolicy.DueAt(Scheduled(-TimeSpan.FromDays(2)), Now));
    }

    [TestMethod]
    public void DueAt_BacksOffBetweenTriesAndGivesUp()
    {
        SavedThread justFailed = Scheduled(-TimeSpan.FromMinutes(1), attempts: 1, lastAttemptAgo: TimeSpan.FromSeconds(30));
        Assert.AreEqual(ScheduleDue.Wait, SchedulePolicy.DueAt(justFailed, Now));

        SavedThread readyAgain = Scheduled(-TimeSpan.FromMinutes(2), attempts: 1, lastAttemptAgo: TimeSpan.FromMinutes(1));
        Assert.AreEqual(ScheduleDue.Post, SchedulePolicy.DueAt(readyAgain, Now));

        SavedThread outOfTries = Scheduled(-TimeSpan.FromMinutes(5), attempts: SchedulePolicy.MaxAttempts, lastAttemptAgo: TimeSpan.FromMinutes(10));
        Assert.AreEqual(ScheduleDue.Missed, SchedulePolicy.DueAt(outOfTries, Now));
    }

    [TestMethod]
    public void RetryDelays_AllFitInsideTheGrace()
    {
        TimeSpan total = TimeSpan.Zero;
        for (int attempts = 1; attempts < SchedulePolicy.MaxAttempts; attempts++)
            total += SchedulePolicy.RetryDelay(attempts);

        Assert.IsTrue(total <= SchedulePolicy.LateGrace, $"{SchedulePolicy.MaxAttempts} tries take {total}, past the {SchedulePolicy.LateGrace} grace");
    }

    [TestMethod]
    public void NextCheck_NoTimerWithoutAnythingWaiting()
    {
        Assert.IsNull(SchedulePolicy.NextCheck([], Now));
        Assert.IsNull(SchedulePolicy.NextCheck([new SavedThread()], Now), "plain drafts don't keep a timer");
        Assert.IsNull(SchedulePolicy.NextCheck([Scheduled(-TimeSpan.FromDays(1))], Now), "missed ones wait for the user, not the timer");
    }

    [TestMethod]
    public void NextCheck_SleepsUntilTheSoonestCappedAtTheMaximum()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(2), SchedulePolicy.NextCheck([Scheduled(TimeSpan.FromHours(3)), Scheduled(TimeSpan.FromMinutes(2))], Now));
        Assert.AreEqual(SchedulePolicy.MaxTimerInterval, SchedulePolicy.NextCheck([Scheduled(TimeSpan.FromHours(3))], Now));
        Assert.AreEqual(SchedulePolicy.MinTimerInterval, SchedulePolicy.NextCheck([Scheduled(-TimeSpan.FromMinutes(1))], Now), "already due: check right away");
    }

    [TestMethod]
    public void NextCheck_WaitsOutTheRetryDelay()
    {
        SavedThread failed = Scheduled(-TimeSpan.FromMinutes(1), attempts: 2, lastAttemptAgo: TimeSpan.FromMinutes(1));
        Assert.AreEqual(SchedulePolicy.RetryDelay(2) - TimeSpan.FromMinutes(1), SchedulePolicy.NextCheck([failed], Now));
    }

    [TestMethod]
    public void Missed_ListsOnlyThoseWaitingForTheUserSoonestFirst()
    {
        SavedThread older = Scheduled(-TimeSpan.FromDays(2));
        SavedThread newer = Scheduled(-TimeSpan.FromHours(1));
        SavedThread due = Scheduled(-TimeSpan.FromMinutes(1));
        SavedThread future = Scheduled(TimeSpan.FromHours(1));

        CollectionAssert.AreEqual(new[] { older, newer }, SchedulePolicy.Missed([newer, due, future, older], Now).ToArray());
    }

    [TestMethod]
    public void ValidateTime_NeedsALittleLeadAndAtMostAYear()
    {
        Assert.IsNotNull(SchedulePolicy.ValidateTime(Now, Now));
        Assert.IsNotNull(SchedulePolicy.ValidateTime(Now - TimeSpan.FromHours(1), Now));
        Assert.IsNull(SchedulePolicy.ValidateTime(Now + TimeSpan.FromMinutes(5), Now));
        Assert.IsNotNull(SchedulePolicy.ValidateTime(Now + TimeSpan.FromDays(400), Now));
    }

    [TestMethod]
    public void FromLocal_SkippedTimeMovesPastTheChange()
    {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 10 March 2030, 2:30 AM doesn't exist in New York: clocks jump from 2:00 to 3:00.
        DateTimeOffset at = SchedulePolicy.FromLocal(new DateTime(2030, 3, 10), new TimeSpan(2, 30, 0), eastern);
        Assert.AreEqual(new DateTimeOffset(2030, 3, 10, 3, 0, 0, TimeSpan.FromHours(-4)), at);
    }

    [TestMethod]
    public void FromLocal_RepeatedTimeMeansTheFirst()
    {
        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 3 November 2030, 1:30 AM happens twice in New York; the first is still on daylight time.
        DateTimeOffset at = SchedulePolicy.FromLocal(new DateTime(2030, 11, 3), new TimeSpan(1, 30, 0), eastern);
        Assert.AreEqual(TimeSpan.FromHours(-4), at.Offset);
        Assert.AreEqual(new DateTimeOffset(2030, 11, 3, 5, 30, 0, TimeSpan.Zero), at.ToUniversalTime());
    }

    [TestMethod]
    public void FromLocal_OrdinaryTimeKeepsTheZonesOffset()
    {
        DateTimeOffset at = SchedulePolicy.FromLocal(new DateTime(2030, 6, 1), new TimeSpan(15, 0, 0), TimeZoneInfo.Utc);
        Assert.AreEqual(new DateTimeOffset(2030, 6, 1, 15, 0, 0, TimeSpan.Zero), at);
    }

    [TestMethod]
    public void DefaultLocalTime_IsTheNextHourAtLeastHalfAnHourAway()
    {
        Assert.AreEqual(new DateTime(2030, 6, 1, 15, 0, 0), SchedulePolicy.DefaultLocalTime(new DateTimeOffset(2030, 6, 1, 14, 20, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
        Assert.AreEqual(new DateTime(2030, 6, 1, 16, 0, 0), SchedulePolicy.DefaultLocalTime(new DateTimeOffset(2030, 6, 1, 14, 40, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
        Assert.AreEqual(new DateTime(2030, 6, 2, 1, 0, 0), SchedulePolicy.DefaultLocalTime(new DateTimeOffset(2030, 6, 1, 23, 40, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
    }

    [TestMethod]
    public void Describe_NamesNearbyDaysAndDatesFurtherOut()
    {
        string Time(DateTimeOffset at) => at.UtcDateTime.ToString("t", EnUs);

        DateTimeOffset today = Now.AddHours(3);
        DateTimeOffset tomorrow = Now.AddDays(1);
        DateTimeOffset yesterday = Now.AddDays(-1);
        DateTimeOffset inFour = Now.AddDays(4);
        DateTimeOffset nextMonth = Now.AddDays(40);
        DateTimeOffset nextYear = Now.AddDays(300);

        Assert.AreEqual($"today at {Time(today)}", SchedulePolicy.Describe(today, Now, TimeZoneInfo.Utc, EnUs));
        Assert.AreEqual($"tomorrow at {Time(tomorrow)}", SchedulePolicy.Describe(tomorrow, Now, TimeZoneInfo.Utc, EnUs));
        Assert.AreEqual($"yesterday at {Time(yesterday)}", SchedulePolicy.Describe(yesterday, Now, TimeZoneInfo.Utc, EnUs));
        Assert.AreEqual($"Wed at {Time(inFour)}", SchedulePolicy.Describe(inFour, Now, TimeZoneInfo.Utc, EnUs));
        Assert.AreEqual($"Jul 11 at {Time(nextMonth)}", SchedulePolicy.Describe(nextMonth, Now, TimeZoneInfo.Utc, EnUs));
        Assert.AreEqual($"Mar 28, 2031 at {Time(nextYear)}", SchedulePolicy.Describe(nextYear, Now, TimeZoneInfo.Utc, EnUs));
    }

    [TestMethod]
    public void Status_SaysWhereEachStands()
    {
        Assert.IsNull(SchedulePolicy.Status(new SavedThread(), Now, TimeZoneInfo.Utc, EnUs));
        StringAssert.StartsWith(SchedulePolicy.Status(Scheduled(TimeSpan.FromHours(2)), Now, TimeZoneInfo.Utc, EnUs), "Scheduled for today at ");
        StringAssert.StartsWith(SchedulePolicy.Status(Scheduled(TimeSpan.Zero), Now, TimeZoneInfo.Utc, EnUs), "Posting");
        StringAssert.StartsWith(SchedulePolicy.Status(Scheduled(-TimeSpan.FromMinutes(2), attempts: 1, lastAttemptAgo: TimeSpan.FromSeconds(10)), Now, TimeZoneInfo.Utc, EnUs), "Trying again");
        StringAssert.StartsWith(SchedulePolicy.Status(Scheduled(-TimeSpan.FromDays(1)), Now, TimeZoneInfo.Utc, EnUs), "Didn't post · was due yesterday at ");

        SavedThread cutOff = Scheduled(-TimeSpan.FromDays(1));
        cutOff.Posts = [new SavedPost(), new SavedPost(), new SavedPost()];
        cutOff.SentCount = 1;
        Assert.AreEqual("1 of 3 posted · the rest didn't go out", SchedulePolicy.Status(cutOff, Now, TimeZoneInfo.Utc, EnUs));
    }

    [TestMethod]
    public void SortGroup_MissedThenScheduledThenDrafts()
    {
        Assert.AreEqual(0, SchedulePolicy.SortGroup(Scheduled(-TimeSpan.FromDays(1)), Now));
        Assert.AreEqual(1, SchedulePolicy.SortGroup(Scheduled(TimeSpan.FromDays(1)), Now));
        Assert.AreEqual(2, SchedulePolicy.SortGroup(new SavedThread(), Now));
        Assert.AreEqual(2, SchedulePolicy.SortGroup(null, Now), "a draft that exists only on Bluesky");
    }

    [TestMethod]
    public void Schedule_AndUnschedule_StartTheTriesAfresh()
    {
        SavedThread thread = Scheduled(-TimeSpan.FromDays(1), attempts: 3, lastAttemptAgo: TimeSpan.FromDays(1));
        thread.LastError = "Couldn't reach Bluesky.";

        SchedulePolicy.Schedule(thread, Now.AddHours(1));
        Assert.AreEqual(Now.AddHours(1), thread.ScheduledAt);
        Assert.AreEqual(0, thread.Attempts);
        Assert.IsNull(thread.LastAttemptAt);
        Assert.IsNull(thread.LastError);

        SchedulePolicy.RecordFailure(thread, Now, "No internet connection.");
        SchedulePolicy.Unschedule(thread);
        Assert.IsNull(thread.ScheduledAt);
        Assert.AreEqual(0, thread.Attempts);
        Assert.IsNull(thread.LastError);
    }

    [TestMethod]
    public void RecordFailure_CountsTheTryAndKeepsTheReason()
    {
        SavedThread thread = Scheduled(TimeSpan.Zero);
        SchedulePolicy.RecordFailure(thread, Now, "No internet connection.");

        Assert.AreEqual(1, thread.Attempts);
        Assert.AreEqual(Now, thread.LastAttemptAt);
        Assert.AreEqual("No internet connection.", SchedulePolicy.Reason(thread));
    }

    [TestMethod]
    public void RecordSent_ResumesAfterThePostThatWentOut()
    {
        var thread = new SavedThread { Posts = [new SavedPost(), new SavedPost()] };
        Assert.IsTrue(SchedulePolicy.CanOpen(thread));

        SchedulePolicy.RecordSent(thread, 0, "at://did:plc:abc/app.bsky.feed.post/1", "bafy1");
        Assert.AreEqual(1, thread.SentCount);
        Assert.AreEqual("at://did:plc:abc/app.bsky.feed.post/1", thread.LastSentUri);
        Assert.AreEqual("bafy1", thread.LastSentCid);
        Assert.IsFalse(SchedulePolicy.CanOpen(thread), "part of it is on Bluesky; the rest goes with Post now");
    }

    [TestMethod]
    public void Reason_DefaultsToNotRunning()
    {
        StringAssert.Contains(SchedulePolicy.Reason(new SavedThread()), "wasn't running");
    }

    [TestMethod]
    public void MissedToast_OneOrMany()
    {
        (string title, string body) = SchedulePolicy.MissedToast([Scheduled(-TimeSpan.FromDays(1))]);
        Assert.AreEqual("A scheduled post didn't go out", title);
        StringAssert.Contains(body, "Hello");

        (title, _) = SchedulePolicy.MissedToast([Scheduled(-TimeSpan.FromDays(1)), Scheduled(-TimeSpan.FromDays(2))]);
        Assert.AreEqual("2 scheduled posts didn't go out", title);
    }

    [TestMethod]
    public void SessionNote_OnlyForBrowserSignInsPastTheirLimit()
    {
        DateTimeOffset farOut = Now + SchedulePolicy.BrowserSessionLimit + TimeSpan.FromDays(1);
        Assert.IsNotNull(SchedulePolicy.SessionNote(browserSession: true, farOut, Now));
        Assert.IsNull(SchedulePolicy.SessionNote(browserSession: true, Now.AddDays(3), Now));
        Assert.IsNull(SchedulePolicy.SessionNote(browserSession: false, farOut, Now), "app-password sessions don't run out");
    }

    [TestMethod]
    public void MissedBanner_EmptyWhenNothingIsMissed()
    {
        Assert.AreEqual(string.Empty, SchedulePolicy.MissedBanner(0));
        StringAssert.StartsWith(SchedulePolicy.MissedBanner(1), "A scheduled post");
        StringAssert.StartsWith(SchedulePolicy.MissedBanner(3), "3 scheduled posts");
    }

    [TestMethod]
    public void SchedulingFields_RoundTrip()
    {
        var original = new SavedThread
        {
            ScheduledAt = Now,
            AccountDid = "did:plc:abc",
            AccountHandle = "someone.bsky.social",
            SentCount = 1,
            LastSentUri = "at://did:plc:abc/app.bsky.feed.post/1",
            LastSentCid = "bafy1",
            Attempts = 2,
            LastAttemptAt = Now,
            LastError = "Couldn't reach Bluesky.",
            QuoteCid = "bafyq",
            LinkCard = false
        };

        SavedThread back = JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(original, SavedThreadJsonContext.Default.SavedThread),
            SavedThreadJsonContext.Default.SavedThread)!;

        Assert.AreEqual(original.AccountDid, back.AccountDid);
        Assert.AreEqual(original.AccountHandle, back.AccountHandle);
        Assert.AreEqual(1, back.SentCount);
        Assert.AreEqual(original.LastSentUri, back.LastSentUri);
        Assert.AreEqual(original.LastSentCid, back.LastSentCid);
        Assert.AreEqual(2, back.Attempts);
        Assert.AreEqual(original.LastAttemptAt, back.LastAttemptAt);
        Assert.AreEqual(original.LastError, back.LastError);
        Assert.AreEqual("bafyq", back.QuoteCid);
        Assert.IsFalse(back.LinkCard);
    }

    [TestMethod]
    public void OlderThreadFiles_GetTheDefaults()
    {
        SavedThread back = JsonSerializer.Deserialize("""{"Id":"abc","Posts":[]}""", SavedThreadJsonContext.Default.SavedThread)!;
        Assert.IsTrue(back.LinkCard, "drafts saved before link cards were recorded keep their card");
        Assert.AreEqual(0, back.SentCount);
        Assert.IsNull(back.ScheduledAt);
    }
}
