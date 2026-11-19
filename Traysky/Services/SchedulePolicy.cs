using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Traysky.Services;

/// <summary>What the scheduler should do with a scheduled thread right now.</summary>
public enum ScheduleDue
{
    /// <summary>Not due yet, or waiting before trying again.</summary>
    Wait,

    /// <summary>Due: post it.</summary>
    Post,

    /// <summary>Too late to post unasked, or out of tries: it waits for the user to decide.</summary>
    Missed
}

/// <summary>
/// Timing and wording for scheduled threads. Bluesky has no scheduling of its own, so Traysky posts a
/// scheduled thread itself, and only while it is running. A thread is posted if Traysky gets to it within
/// <see cref="LateGrace"/> of its time (a PC that wakes a few minutes late still posts). Past that it is
/// never posted without the user saying so: it shows as missed and the next launch asks about it.
/// Pure .NET, so tests cover it.
/// </summary>
public static class SchedulePolicy
{
    /// <summary>How late a scheduled thread may still go out on its own.</summary>
    public static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(15);

    /// <summary>Tries before a thread that keeps failing is left for the user.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The soonest a post can be scheduled for, so it isn't due before the box has even cleared.</summary>
    public static readonly TimeSpan MinLead = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxLead = TimeSpan.FromDays(366);

    /// <summary>The longest the scheduler's timer sleeps, so a wake it missed (sleep, a clock change) is caught within this.</summary>
    public static readonly TimeSpan MaxTimerInterval = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MinTimerInterval = TimeSpan.FromSeconds(1);

    /// <summary>The wait after the <paramref name="attempts"/>th failed try. All five fit inside <see cref="LateGrace"/>.</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 0 => TimeSpan.Zero,
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(3),
        _ => TimeSpan.FromMinutes(5)
    };

    public static ScheduleDue DueAt(SavedThread thread, DateTimeOffset now)
    {
        if (thread.ScheduledAt is not { } at || at > now)
            return ScheduleDue.Wait;

        if (thread.Attempts >= MaxAttempts || now - at > LateGrace)
            return ScheduleDue.Missed;

        return thread.LastAttemptAt is { } last && now < last + RetryDelay(thread.Attempts)
            ? ScheduleDue.Wait
            : ScheduleDue.Post;
    }

    /// <summary>Whether it waits for the user: shown in the warning color with the reason, and asked about at launch.</summary>
    public static bool NeedsAttention(SavedThread thread, DateTimeOffset now) => DueAt(thread, now) == ScheduleDue.Missed;

    /// <summary>Scheduled threads left for the user (too late, or out of tries), soonest first.</summary>
    public static IReadOnlyList<SavedThread> Missed(IEnumerable<SavedThread> threads, DateTimeOffset now) =>
        threads.Where(t => NeedsAttention(t, now)).OrderBy(t => t.ScheduledAt).ToList();

    /// <summary>
    /// How long the scheduler may sleep: until the next thread is due or may be tried again, capped at
    /// <see cref="MaxTimerInterval"/>. Null when nothing is waiting, so no timer runs at all.
    /// </summary>
    public static TimeSpan? NextCheck(IEnumerable<SavedThread> threads, DateTimeOffset now)
    {
        DateTimeOffset? next = null;
        foreach (SavedThread thread in threads)
        {
            if (thread.ScheduledAt is not { } at || NeedsAttention(thread, now))
                continue;

            DateTimeOffset when = at;
            if (thread.LastAttemptAt is { } last && last + RetryDelay(thread.Attempts) > when)
                when = last + RetryDelay(thread.Attempts);

            if (next is null || when < next)
                next = when;
        }

        if (next is not { } soonest)
            return null;

        TimeSpan wait = soonest - now;
        return wait < MinTimerInterval ? MinTimerInterval : wait > MaxTimerInterval ? MaxTimerInterval : wait;
    }

    /// <summary>Why a time can't be used, or null when it can.</summary>
    public static string? ValidateTime(DateTimeOffset at, DateTimeOffset now) =>
        at < now + MinLead ? "Pick a time at least a minute from now."
        : at > now + MaxLead ? "Pick a time within the next year."
        : null;

    /// <summary>
    /// A date and time on the wall clock of <paramref name="zone"/>, as an instant. A time a clock change
    /// skips moves to just after the change; a time that happens twice means the first one.
    /// </summary>
    public static DateTimeOffset FromLocal(DateTime date, TimeSpan time, TimeZoneInfo zone)
    {
        DateTime local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        for (int i = 0; i < 16 && zone.IsInvalidTime(local); i++)
            local = local.AddMinutes(15);

        TimeSpan offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>Where the picker starts: the next whole hour that is at least half an hour away, on the wall clock of <paramref name="zone"/>.</summary>
    public static DateTime DefaultLocalTime(DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateTime hour = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0).AddHours(1);
        return hour - local < TimeSpan.FromMinutes(30) ? hour.AddHours(1) : hour;
    }

    /// <summary>"today at 3:00 PM", "tomorrow at 9:00 AM", "Tue at 3:00 PM", "Nov 25 at 3:00 PM", on the wall clock of <paramref name="zone"/>.</summary>
    public static string Describe(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        DateTime local = TimeZoneInfo.ConvertTime(at, zone).DateTime;
        DateTime today = TimeZoneInfo.ConvertTime(now, zone).DateTime.Date;
        int days = (local.Date - today).Days;

        string day = days switch
        {
            0 => "today",
            1 => "tomorrow",
            -1 => "yesterday",
            >= -6 and <= 6 => local.ToString("ddd", culture),
            _ when local.Year == today.Year => local.ToString("MMM d", culture),
            _ => local.ToString("MMM d, yyyy", culture)
        };

        return $"{day} at {local.ToString("t", culture)}";
    }

    /// <summary>The line a scheduled thread shows in the drafts list; null for a plain draft.</summary>
    public static string? Status(SavedThread thread, DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        if (thread.ScheduledAt is not { } at)
            return null;

        string when = Describe(at, now, zone, culture);
        return DueAt(thread, now) switch
        {
            ScheduleDue.Missed when thread.SentCount > 0 => $"{thread.SentCount} of {thread.Posts.Count} posted · the rest didn't go out",
            ScheduleDue.Missed => $"Didn't post · was due {when}",
            _ when at > now => $"Scheduled for {when}",
            _ => thread.Attempts > 0 ? $"Trying again · was due {when}" : $"Posting · due {when}"
        };
    }

    /// <summary>Said when a scheduled thread's copy in Bluesky drafts is gone (posted or deleted in another app), which cancels it.</summary>
    public const string CancelledNote = "Not posted: its copy in your Bluesky drafts was posted or deleted in another app. It's kept here as a draft.";

    /// <summary>Why it is waiting for the user, for the drafts list and the toast.</summary>
    public static string Reason(SavedThread thread) =>
        thread.LastError ?? "It was due while Traysky wasn't running.";

    /// <summary>Where it sits in the drafts list: ones waiting for a decision first, then what's coming up, then plain drafts.</summary>
    public static int SortGroup(SavedThread? thread, DateTimeOffset now) =>
        thread?.ScheduledAt is null ? 2 : NeedsAttention(thread, now) ? 0 : 1;

    /// <summary>A thread that is partly on Bluesky can't go back into the compose box: the rest is sent with Post now, so it stays one thread.</summary>
    public static bool CanOpen(SavedThread thread) => thread.SentCount == 0;

    public static void Schedule(SavedThread thread, DateTimeOffset at)
    {
        thread.ScheduledAt = at;
        ResetTries(thread);
    }

    public static void Unschedule(SavedThread thread)
    {
        thread.ScheduledAt = null;
        ResetTries(thread);
    }

    /// <summary>A fresh start, for a new time or the user saying "post now".</summary>
    public static void ResetTries(SavedThread thread)
    {
        thread.Attempts = 0;
        thread.LastAttemptAt = null;
        thread.LastError = null;
    }

    public static void RecordFailure(SavedThread thread, DateTimeOffset now, string error)
    {
        thread.Attempts++;
        thread.LastAttemptAt = now;
        thread.LastError = error;
    }

    /// <summary>Notes that post <paramref name="index"/> is on Bluesky, so a retry continues after it.</summary>
    public static void RecordSent(SavedThread thread, int index, string uri, string cid)
    {
        thread.SentCount = index + 1;
        thread.LastSentUri = uri;
        thread.LastSentCid = cid;
    }

    /// <summary>How long Bluesky lets a browser (OAuth) sign-in last before it has to be done again.</summary>
    public static readonly TimeSpan BrowserSessionLimit = TimeSpan.FromDays(14);

    /// <summary>
    /// Said after scheduling past the point a browser sign-in is sure to have ended: posting needs the
    /// account signed in at that time. Null for app-password sessions, or when it's soon enough.
    /// </summary>
    public static string? SessionNote(bool browserSession, DateTimeOffset at, DateTimeOffset now) =>
        browserSession && at > now + BrowserSessionLimit
            ? "Browser sign-ins end after two weeks, so sign in again before then or it won't post."
            : null;

    /// <summary>The compose page's bar while scheduled threads wait for the user.</summary>
    public static string MissedBanner(int count) => count switch
    {
        <= 0 => string.Empty,
        1 => "A scheduled post didn't go out. Post it now or change it from Drafts.",
        _ => $"{count} scheduled posts didn't go out. Post them now or change them from Drafts."
    };

    /// <summary>The toast for threads newly found waiting for the user.</summary>
    public static (string Title, string Body) MissedToast(IReadOnlyList<SavedThread> missed) => missed.Count == 1
        ? ("A scheduled post didn't go out", $"{SavedThreadPolicy.Preview(missed[0])} · {Reason(missed[0])} Open Drafts to post it or change it.")
        : ($"{missed.Count} scheduled posts didn't go out", "Open Drafts to post them now or change them.");

    /// <summary>The toast once a scheduled thread is up.</summary>
    public static (string Title, string Body) SentToast(SavedThread thread, bool settingsApplied) => (
        thread.Posts.Count > 1 ? "Your scheduled thread is posted" : "Your scheduled post is up",
        settingsApplied
            ? SavedThreadPolicy.Preview(thread)
            : $"{SavedThreadPolicy.Preview(thread)} · Its reply and quote settings didn't apply; set them on the post.");
}
