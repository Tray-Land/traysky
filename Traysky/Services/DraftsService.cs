using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky.Drafts;
using idunno.Bluesky.Feed.Gates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Saved drafts, kept on the server so bsky.app and other clients see them too. A draft holds the text,
/// a quoted post and the reply / quote controls; picked images and video are not stored (see
/// <see cref="DraftPolicy"/>). Calls return null / false when the server refused, and log the reason.
/// </summary>
public static class DraftsService
{
    private const int PageSize = 50;

    public static string DeviceId => DraftPolicy.DeviceId(Environment.MachineName);

    /// <summary>Every draft on the account, newest first. Null when the lookup failed.</summary>
    public static async Task<List<DraftItem>?> GetAllAsync()
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        List<DraftItem> all = [];
        try
        {
            string? cursor = null;
            do
            {
                var result = await BlueskySessionService.Instance.Agent.GetDrafts(PageSize, cursor, CancellationToken.None);
                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("Drafts", $"GetDrafts failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    return null;
                }

                all.AddRange(result.Result.Select(ToItem));
                cursor = result.Result.Cursor;
            }
            while (!string.IsNullOrEmpty(cursor));
        }
        catch (Exception ex)
        {
            LogService.Warn("Drafts", $"GetDrafts threw: {ex.Message}");
            return null;
        }
        return all.OrderByDescending(d => d.SortKey).ToList();
    }

    /// <summary>Saves a new draft and returns its id, or null on failure.</summary>
    public static async Task<TimestampIdentifier?> CreateAsync(string text, StrongReference? quote, ReplyChoice reply, bool allowQuotes)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.CreateDraft(Build(text, quote, reply, allowQuotes), CancellationToken.None);
            if (result.Succeeded && result.Result is not null)
                return result.Result;

            LogService.Warn("Drafts", $"CreateDraft failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Drafts", $"CreateDraft threw: {ex.Message}");
        }
        return null;
    }

    /// <summary>Rewrites an existing draft in place (a restored draft that was edited and saved again).</summary>
    public static async Task<bool> UpdateAsync(TimestampIdentifier id, string text, StrongReference? quote, ReplyChoice reply, bool allowQuotes)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.UpdateDraft(
                new DraftWithId(id, Build(text, quote, reply, allowQuotes)), CancellationToken.None);
            if (result.Succeeded)
                return true;

            LogService.Warn("Drafts", $"UpdateDraft failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Drafts", $"UpdateDraft threw: {ex.Message}");
        }
        return false;
    }

    public static async Task<bool> DeleteAsync(TimestampIdentifier id)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.DeleteDraft(id, CancellationToken.None);
            if (result.Succeeded)
                return true;

            LogService.Warn("Drafts", $"DeleteDraft failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Drafts", $"DeleteDraft threw: {ex.Message}");
        }
        return false;
    }

    private static Draft Build(string text, StrongReference? quote, ReplyChoice reply, bool allowQuotes)
    {
        DraftPost post = quote is null
            ? new DraftPost(text)
            : new DraftPost(text, (idunno.Bluesky.PostSelfLabels?)null, null, null, null, null, new List<DraftEmbedRecord> { new(quote) });

        IReadOnlyList<GateRule>? rules = InteractionGatePolicy.ToRules(reply);

        // A missing list means "no gate" (everyone / quotes allowed); an empty one on the thread gate means nobody.
        IReadOnlyList<ThreadGateRule>? threadRules = rules is null ? null : GatesService.ToThreadRules(rules).ToList();
        IReadOnlyList<PostGateRule>? postRules = allowQuotes ? null : new List<PostGateRule> { new DisableEmbeddingRule() };

        return new Draft(new List<DraftPost> { post }, DeviceId, Environment.MachineName, null!, postRules!, threadRules!);
    }

    private static DraftItem ToItem(DraftView view)
    {
        Draft draft = view.Draft;
        DraftPost? first = draft.Posts?.FirstOrDefault();

        int images = (first?.EmbedImages?.Count ?? 0) + (first?.EmbedGallery?.Items?.Count ?? 0);
        int videos = first?.EmbedVideos?.Count ?? 0;

        return new DraftItem
        {
            Id = view.Id,
            Text = first?.Text ?? string.Empty,
            Quote = first?.EmbedRecords?.FirstOrDefault()?.Record,
            Reply = InteractionGatePolicy.FromRules(GatesService.FromThreadRules(draft.ThreadGateAllowRules)),
            AllowQuotes = draft.PostGateEmbeddingRules?.Any(r => r is DisableEmbeddingRule) != true,
            Age = RelativeTimeFormatter.Format(view.UpdatedAt, DateTimeOffset.UtcNow),
            Note = DraftPolicy.RestoreNote(images, videos),
            SortKey = view.UpdatedAt
        };
    }
}
