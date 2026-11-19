using CommunityToolkit.Mvvm.ComponentModel;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using idunno.Bluesky.Embed;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Posts scheduled threads when their time comes.
/// <para>
/// This is background work on purpose, the exception the tray conventions allow for something the
/// user explicitly started: they asked for this post to go out at this time, and Bluesky has no
/// scheduling of its own, so Traysky has to send it, flyout open or not. It stops when its purpose
/// does (nothing scheduled means no timer at all) and is as cheap as it can be: no polling and no
/// network until a post is due; one one-shot timer armed for the next due time, capped at
/// <see cref="SchedulePolicy.MaxTimerInterval"/> so sleep, resume and clock changes correct
/// themselves; media is read from disk only to post it. <see cref="App"/> owns it (the flyout comes
/// and goes): started once the session is restored, stopped on exit.
/// </para>
/// <para>
/// A thread more than <see cref="SchedulePolicy.LateGrace"/> late, or out of tries, is never posted
/// unasked: it is announced (a toast, and a bar on the compose page) and waits in Drafts for Post now
/// or editing. Progress is saved after every post, so a retry or a crash never posts one twice.
/// </para>
/// </summary>
public sealed partial class ScheduledPostService : ObservableObject
{
    private static readonly Lazy<ScheduledPostService> _instance = new(() => new ScheduledPostService());

    public static ScheduledPostService Instance => _instance.Value;

    private const string MissedToastTag = "scheduled-missed";

    private readonly DispatcherQueueTimer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Missed threads already announced this session, so each is toasted once per launch.</summary>
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

    private bool _started;

    /// <summary>Set when a check was asked for while one was running, so it runs again with what changed.</summary>
    private bool _recheck;

    private string? _publishingId;

    private ScheduledPostService()
    {
        DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("ScheduledPostService must be created on the UI thread.");

        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => _ = CheckAsync();

        // Threads waiting on a sign-in can go out as soon as there is one.
        BlueskySessionService.Instance.SignedIn += (_, _) => Refresh();
    }

    /// <summary>Scheduled threads waiting for the user to decide (too late, or out of tries).</summary>
    [ObservableProperty]
    public partial int MissedCount { get; private set; }

    /// <summary>Raised on the UI thread when a scheduled thread was posted, failed, was cancelled or became missed, so the drafts list can catch up.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether that thread is being posted right now, so the compose box leaves it alone.</summary>
    public bool IsPublishing(string? id) => id is not null && string.Equals(_publishingId, id, StringComparison.Ordinal);

    public Task StartAsync()
    {
        if (_started || PreviewMode.IsEnabled)
            return Task.CompletedTask;

        _started = true;
        return CheckAsync();
    }

    public void Stop()
    {
        _started = false;
        _timer.Stop();
    }

    /// <summary>Something changed what's scheduled (the compose box scheduled, opened or deleted a thread): look again.</summary>
    public void Refresh()
    {
        if (_started)
            _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (!_started)
            return;

        if (!await _gate.WaitAsync(0))
        {
            _recheck = true;
            return;
        }

        bool changed = false;
        try
        {
            _recheck = false;
            _timer.Stop();

            List<SavedThread> threads = await SavedThreadStore.ListAsync();
            foreach (SavedThread listed in SavedThreadPolicy.Overdue(threads, DateTimeOffset.UtcNow))
            {
                if (!_started || SchedulePolicy.DueAt(listed, DateTimeOffset.UtcNow) != ScheduleDue.Post)
                    continue;

                // Read again just before posting: an earlier post in this loop can take minutes (a video),
                // and meanwhile the thread may have been opened to edit (which unschedules it) or deleted.
                SavedThread? thread = await SavedThreadStore.FindAsync(listed.Id);
                if (thread is null || SchedulePolicy.DueAt(thread, DateTimeOffset.UtcNow) != ScheduleDue.Post)
                    continue;

                await PublishAsync(thread);
                changed = true;
            }

            if (changed)
                threads = await SavedThreadStore.ListAsync();

            DateTimeOffset now = DateTimeOffset.UtcNow;
            changed |= Announce(threads, now);
            Arm(SchedulePolicy.NextCheck(threads, now));
        }
        catch (Exception ex)
        {
            LogService.Error("Schedule", "Checking scheduled posts threw", ex);
            Arm(SchedulePolicy.MaxTimerInterval);
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);

        if (_recheck)
            _ = CheckAsync();
    }

    private void Arm(TimeSpan? wait)
    {
        _timer.Stop();
        if (!_started || wait is not { } interval)
            return;

        _timer.Interval = interval;
        _timer.Start();
    }

    /// <summary>Counts the threads waiting for the user and toasts the ones not announced yet this launch. True when anything changed.</summary>
    private bool Announce(List<SavedThread> threads, DateTimeOffset now)
    {
        IReadOnlyList<SavedThread> missed = SchedulePolicy.Missed(threads, now);
        int before = MissedCount;
        MissedCount = missed.Count;

        // A thread that was rescheduled and then missed again deserves a new toast.
        _announced.IntersectWith(missed.Select(t => t.Id));
        List<SavedThread> fresh = missed.Where(t => _announced.Add(t.Id)).ToList();
        if (fresh.Count > 0)
        {
            (string title, string body) = SchedulePolicy.MissedToast(fresh);
            ToastService.Show(title, body, ShellDestination.Compose, tag: MissedToastTag);
        }

        return fresh.Count > 0 || before != MissedCount;
    }

    /// <summary>
    /// The user said to post it now: whatever its time, lateness or tries. Returns null once it is
    /// posted, or what stopped it.
    /// </summary>
    public async Task<string?> PostNowAsync(string id)
    {
        string? message;
        await _gate.WaitAsync();
        try
        {
            SavedThread? thread = await SavedThreadStore.FindAsync(id);
            if (thread is null)
                return "It's no longer saved on this PC. It may have just been posted.";

            SchedulePolicy.ResetTries(thread);
            (PublishResult result, string? error) = await PublishAsync(thread);
            message = result switch
            {
                PublishResult.Sent => null,
                PublishResult.Cancelled => SchedulePolicy.CancelledNote,
                _ => error
            };
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        Refresh();
        return message;
    }

    private enum PublishResult
    {
        Sent,
        Cancelled,
        Failed
    }

    /// <summary>Posts what's left of a thread, recording any failure on it. Never throws.</summary>
    private async Task<(PublishResult Result, string? Error)> PublishAsync(SavedThread thread)
    {
        _publishingId = thread.Id;
        try
        {
            (PublishResult result, string? error, bool keepTrying) = await TryPublishAsync(thread);
            if (result != PublishResult.Failed)
                return (result, null);

            await RecordFailureAsync(thread, error ?? "Couldn't post it.", keepTrying);
            return (result, error);
        }
        catch (Exception ex)
        {
            LogService.Error("Schedule", $"Posting scheduled thread {thread.Id} threw", ex);
            await RecordFailureAsync(thread, "Something went wrong while posting it.", keepTrying: true);
            return (PublishResult.Failed, "Something went wrong while posting it.");
        }
        finally
        {
            _publishingId = null;

            // The bytes were only needed to upload; don't hold a video in memory until the next GC of this list.
            foreach (SavedMedia media in thread.Posts.SelectMany(p => p.Media))
                media.Bytes = null;
        }
    }

    /// <summary>
    /// Notes a failed try. One that trying again won't fix (missing media, a broken reference) is left
    /// for the user straight away. Recorded on a fresh copy, since loading media drops what's missing
    /// and that must not be saved as if the thread never had it.
    /// </summary>
    private static async Task RecordFailureAsync(SavedThread thread, string error, bool keepTrying)
    {
        SavedThread record = await SavedThreadStore.FindAsync(thread.Id) ?? thread;
        SchedulePolicy.RecordFailure(record, DateTimeOffset.UtcNow, error);
        if (!keepTrying)
            record.Attempts = SchedulePolicy.MaxAttempts;

        await SavedThreadStore.SaveDetailsAsync(record);
        LogService.Warn("Schedule", $"Scheduled thread {thread.Id} not posted (try {record.Attempts}): {error}");
    }

    private static async Task<(PublishResult Result, string? Error, bool KeepTrying)> TryPublishAsync(SavedThread thread)
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        string signIn = thread.AccountHandle is { Length: > 0 } handle ? $"Sign in as @{handle} to post it." : "Sign in to post it.";

        if (!session.IsSignedIn || (thread.AccountDid is { } did && !string.Equals(session.Did, did, StringComparison.Ordinal)))
            return (PublishResult.Failed, signIn, true);

        if (!NetworkStatusService.IsInternetAvailable())
            return (PublishResult.Failed, "No internet connection.", true);

        if (!await session.EnsureAuthenticatedAsync())
            return (PublishResult.Failed, session.IsSignedIn ? "Couldn't reach Bluesky." : signIn, true);

        // Its copy in Bluesky drafts being gone means it was posted or deleted from another app: that cancels it.
        if (thread.SentCount == 0 && thread.BlueskyDraftId is { } mirrorId)
        {
            List<DraftItem>? drafts = await DraftsService.GetAllAsync();
            if (drafts is null)
                return (PublishResult.Failed, "Couldn't reach Bluesky.", true);

            if (!drafts.Any(d => string.Equals(d.Id?.ToString(), mirrorId, StringComparison.Ordinal)))
            {
                await CancelAsync(thread);
                return (PublishResult.Cancelled, null, false);
            }
        }

        if (!await SavedThreadStore.LoadMediaAsync(thread))
            return (PublishResult.Failed, "Some of its images or video are no longer on this PC. Open it to check, then post it.", false);

        BlueskyAgent agent = session.Agent;
        int start = thread.SentCount;
        if (start >= thread.Posts.Count)
        {
            await FinishAsync(thread, settingsApplied: true);
            return (PublishResult.Sent, null, false);
        }

        // Every remaining post's media goes up before any record is created, so a failed upload leaves nothing half-posted.
        var media = new List<PreparedMedia>(thread.Posts.Count - start);
        for (int i = start; i < thread.Posts.Count; i++)
        {
            MediaResult prepared = await PostPublisher.PrepareMediaAsync(agent, thread.Posts[i].Media.Select(ToUpload).ToList(), reporter: null, CancellationToken.None);
            if (prepared.Media is null)
                return (PublishResult.Failed, prepared.Error ?? "Couldn't upload its media.", true);

            media.Add(prepared.Media);
        }

        string[] langs = [.. ComposeLanguagePolicy.Resolve(string.Join(',', thread.Langs), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)];
        bool settingsApplied = true;

        for (int i = start; i < thread.Posts.Count; i++)
        {
            SavedPost post = thread.Posts[i];
            PreparedMedia postMedia = media[i - start];
            bool isRoot = i == 0;

            StrongReference? parent = null;
            if (!isRoot && !TryReference(thread.LastSentUri, thread.LastSentCid, out parent))
                return (PublishResult.Failed, "Lost track of the post before this one, so the rest can't be added to the thread.", false);

            StrongReference? quote = null;
            if (isRoot && !string.IsNullOrEmpty(thread.QuoteUri) && !TryReference(thread.QuoteUri, thread.QuoteCid, out quote))
                return (PublishResult.Failed, "The post it quotes couldn't be found. Open it to check, then post it.", false);

            EmbeddedExternal? card = null;
            if (isRoot && thread.LinkCard && !postMedia.HasMedia && quote is null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                card = await PostPublisher.TryLinkCardAsync(agent, post.Text, timeout.Token);
            }

            AtProtoHttpResult<CreateRecordResult> result = await PostPublisher.PostAsync(
                agent, post.Text, langs, SavedThreadPolicy.LabelOf(post), postMedia, card, parent, quote);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Schedule", $"Scheduled post {i + 1}/{thread.Posts.Count} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error} {result.AtErrorDetail?.Message}");
                return (PublishResult.Failed, PostPublisher.FailureText(result.StatusCode, result.AtErrorDetail?.Message), true);
            }

            // Saved before anything else, so neither a retry nor a crash from here posts this one again.
            string? mirror = isRoot ? thread.BlueskyDraftId : null;
            SchedulePolicy.RecordSent(thread, i, result.Result.Uri.ToString(), result.Result.Cid.ToString());
            if (isRoot)
                thread.BlueskyDraftId = null;
            await SavedThreadStore.SaveDetailsAsync(thread);

            if (isRoot)
            {
                // bsky.app could post the mirrored draft a second time; it has served its purpose.
                if (mirror is not null && TimestampIdentifier.TryParse(mirror, out TimestampIdentifier? mirrorTid) && mirrorTid is not null)
                    await DraftsService.DeleteAsync(mirrorTid);

                ReplyChoice reply = SavedThreadPolicy.ReplyOf(thread);
                if (!InteractionGatePolicy.IsDefault(reply, thread.AllowQuotes))
                    settingsApplied = await GatesService.ApplyToNewPostAsync(result.Result.Uri.ToString(), reply, thread.AllowQuotes);
            }
        }

        await FinishAsync(thread, settingsApplied);
        return (PublishResult.Sent, null, false);
    }

    private static async Task FinishAsync(SavedThread thread, bool settingsApplied)
    {
        await SavedThreadStore.DeleteAsync(thread.Id);
        LogService.Info("Schedule", $"Posted scheduled thread {thread.Id} ({thread.Posts.Count} posts)");

        (string title, string body) = SchedulePolicy.SentToast(thread, settingsApplied);
        ToastService.Show(title, body, ShellDestination.Home, tag: "scheduled-" + thread.Id[..Math.Min(thread.Id.Length, 16)]);
    }

    /// <summary>Takes it off the schedule and keeps it as a plain draft on this PC, saying why.</summary>
    private static async Task CancelAsync(SavedThread thread)
    {
        SchedulePolicy.Unschedule(thread);
        thread.BlueskyDraftId = null;
        thread.LastError = SchedulePolicy.CancelledNote;
        await SavedThreadStore.SaveDetailsAsync(thread);
        LogService.Info("Schedule", $"Scheduled thread {thread.Id} cancelled: its Bluesky draft is gone");

        ToastService.Show("A scheduled post was cancelled", $"{SavedThreadPolicy.Preview(thread)} · {SchedulePolicy.CancelledNote}", ShellDestination.Compose, tag: MissedToastTag);
    }

    private static MediaUpload ToUpload(SavedMedia m) =>
        new(m.Bytes ?? [], m.MimeType, m.FileName, m.IsVideo, m.AltText, m.PixelWidth, m.PixelHeight);

    private static bool TryReference(string? uri, string? cid, [NotNullWhen(true)] out StrongReference? reference)
    {
        reference = null;
        if (string.IsNullOrEmpty(uri) || string.IsNullOrEmpty(cid)
            || !AtUri.TryParse(uri, out AtUri? atUri) || atUri is null
            || !Cid.TryParse(cid, out Cid? parsed) || parsed is null)
        {
            return false;
        }

        reference = new StrongReference(atUri, parsed);
        return true;
    }
}
