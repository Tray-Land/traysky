using idunno.AtProto;
using idunno.Bluesky.Feed.Gates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// Reply controls (thread gates) and quote controls (post gates). Reads and writes the gate records
/// for a post, translating between idunno's rule classes and <see cref="InteractionGatePolicy"/>'s
/// neutral ones. Calls return null / false when the server refused, and log the reason.
/// </summary>
public static class GatesService
{
    /// <summary>The state of a post's controls. <see cref="Reply"/> is null when the post isn't a thread root, so it has none.</summary>
    public sealed record State(ReplyChoice? Reply, bool AllowQuotes);

    /// <summary>Reads the gates on a post. Null when the lookup failed; a post with no gate record is the default (everyone, quotes allowed).</summary>
    public static async Task<State?> LoadAsync(string postUri, bool includeReplyGate)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            AtUri post = new(postUri);

            ReplyChoice? reply = null;
            if (includeReplyGate)
            {
                var threadGate = await agent.GetThreadGate(post, CancellationToken.None);
                if (threadGate.Succeeded && threadGate.Result is not null)
                    reply = InteractionGatePolicy.FromRules(FromThreadRules(threadGate.Result.Rules));
                else if (IsMissing(threadGate.StatusCode))
                    reply = ReplyChoice.Everyone;
                else
                {
                    LogService.Warn("Gates", $"GetThreadGate failed: {(int)threadGate.StatusCode} {threadGate.AtErrorDetail?.Error}");
                    return null;
                }
            }

            bool allowQuotes = true;
            var postGate = await agent.GetPostGate(post, CancellationToken.None);
            if (postGate.Succeeded && postGate.Result is not null)
                allowQuotes = postGate.Result.Rules?.Any(r => r is DisableEmbeddingRule) != true;
            else if (!IsMissing(postGate.StatusCode))
            {
                LogService.Warn("Gates", $"GetPostGate failed: {(int)postGate.StatusCode} {postGate.AtErrorDetail?.Error}");
                return null;
            }

            return new State(reply, allowQuotes);
        }
        catch (Exception ex)
        {
            LogService.Warn("Gates", $"Loading gates threw: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes the gates for a post that was just created, skipping whichever is still the default. False if either write failed.</summary>
    public static async Task<bool> ApplyToNewPostAsync(string postUri, ReplyChoice? reply, bool allowQuotes)
    {
        bool ok = true;
        AtUri post = new(postUri);

        if (reply is not null && InteractionGatePolicy.NeedsThreadGate(reply))
            ok &= await WriteThreadGateAsync(post, reply, existing: null);
        if (InteractionGatePolicy.NeedsPostGate(allowQuotes))
            ok &= await WritePostGateAsync(post, allowQuotes, existing: null);

        return ok;
    }

    /// <summary>Sets who can reply to a thread root, creating, updating or removing its thread gate as the choice needs.</summary>
    public static async Task<bool> SetReplyChoiceAsync(string postUri, ReplyChoice choice)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            AtUri post = new(postUri);
            var current = await BlueskySessionService.Instance.Agent.GetThreadGate(post, CancellationToken.None);
            ThreadGate? existing = current.Succeeded ? current.Result : null;
            if (existing is null && !IsMissing(current.StatusCode))
            {
                LogService.Warn("Gates", $"GetThreadGate failed: {(int)current.StatusCode} {current.AtErrorDetail?.Error}");
                return false;
            }

            return await WriteThreadGateAsync(post, choice, existing);
        }
        catch (Exception ex)
        {
            LogService.Warn("Gates", $"Setting reply controls threw: {ex.Message}");
            return false;
        }
    }

    /// <summary>Turns quoting a post on or off, creating, updating or removing its post gate as needed.</summary>
    public static async Task<bool> SetAllowQuotesAsync(string postUri, bool allowQuotes)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            AtUri post = new(postUri);
            var current = await BlueskySessionService.Instance.Agent.GetPostGate(post, CancellationToken.None);
            PostGate? existing = current.Succeeded ? current.Result : null;
            if (existing is null && !IsMissing(current.StatusCode))
            {
                LogService.Warn("Gates", $"GetPostGate failed: {(int)current.StatusCode} {current.AtErrorDetail?.Error}");
                return false;
            }

            return await WritePostGateAsync(post, allowQuotes, existing);
        }
        catch (Exception ex)
        {
            LogService.Warn("Gates", $"Setting quote controls threw: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Detaches <paramref name="quotingPostUri"/> from the viewer's <paramref name="quotedPostUri"/> by listing it in the
    /// quoted post's post gate, leaving that gate's rules alone. One-way here: the quote stays detached.
    /// True when it is detached afterwards (including when it already was).
    /// </summary>
    public static async Task<bool> DetachQuoteAsync(string quotedPostUri, string quotingPostUri)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            AtUri post = new(quotedPostUri);

            var current = await agent.GetPostGate(post, CancellationToken.None);
            PostGate? existing = current.Succeeded ? current.Result : null;
            if (existing is null && !IsMissing(current.StatusCode))
            {
                LogService.Warn("Gates", $"GetPostGate failed: {(int)current.StatusCode} {current.AtErrorDetail?.Error}");
                return false;
            }

            var (merge, detached) = DetachQuotePolicy.Merge(
                existing?.DetachedEmbeddingUris?.Select(u => u.ToString()),
                quotingPostUri,
                idunno.Bluesky.Maximum.PostGateDetachedEmbeddingPosts);

            if (merge == DetachQuotePolicy.MergeResult.AlreadyDetached)
                return true;
            if (merge == DetachQuotePolicy.MergeResult.LimitReached)
            {
                LogService.Warn("Gates", "Detach refused: the post gate already lists the maximum number of detached quotes");
                return false;
            }

            List<AtUri> uris = detached.Select(u => new AtUri(u)).ToList();
            ICollection<PostGateRule> rules = existing?.Rules is { } r ? [.. r] : [];
            PostGate gate = new(post, rules, uris);

            return existing is null
                ? Check(await agent.AddPostGate(gate, CancellationToken.None), "AddPostGate")
                : Check(await agent.UpdatePostGate(gate, CancellationToken.None), "UpdatePostGate");
        }
        catch (Exception ex)
        {
            LogService.Warn("Gates", $"Detaching a quote threw: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> WriteThreadGateAsync(AtUri post, ReplyChoice choice, ThreadGate? existing)
    {
        var agent = BlueskySessionService.Instance.Agent;
        IReadOnlyList<GateRule>? rules = InteractionGatePolicy.ToRules(choice);

        try
        {
            if (rules is null)
            {
                if (existing is null)
                    return true;

                // Hidden replies live on the same record; keep the gate if any are set.
                if (existing.HiddenReplies is { Count: > 0 })
                    return Check(await agent.UpdateThreadGate(new ThreadGate(post, null!, existing.HiddenReplies), CancellationToken.None), "UpdateThreadGate");

                return Check(await agent.DeleteThreadGate(post, CancellationToken.None), "DeleteThreadGate");
            }

            ICollection<AtUri> hidden = existing?.HiddenReplies ?? [];
            ThreadGate gate = rules.Count == 0 && hidden.Count == 0
                ? ThreadGate.NoReplies(post)
                : new ThreadGate(post, ToThreadRules(rules), hidden);

            return existing is null
                ? Check(await agent.AddThreadGate(gate, CancellationToken.None), "AddThreadGate")
                : Check(await agent.UpdateThreadGate(gate, CancellationToken.None), "UpdateThreadGate");
        }
        catch (Exception ex)
        {
            LogService.Warn("Gates", $"Writing the thread gate threw: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> WritePostGateAsync(AtUri post, bool allowQuotes, PostGate? existing)
    {
        var agent = BlueskySessionService.Instance.Agent;

        try
        {
            // Quotes the author already detached stay detached whichever way the switch goes.
            ICollection<AtUri> detached = existing?.DetachedEmbeddingUris ?? [];

            if (allowQuotes)
            {
                if (existing is null)
                    return true;

                return detached.Count > 0
                    ? Check(await agent.UpdatePostGate(new PostGate(post, new List<PostGateRule>(), detached), CancellationToken.None), "UpdatePostGate")
                    : Check(await agent.DeletePostGate(post, CancellationToken.None), "DeletePostGate");
            }

            PostGate gate = new(post, new List<PostGateRule> { new DisableEmbeddingRule() }, detached);
            return existing is null
                ? Check(await agent.AddPostGate(gate, CancellationToken.None), "AddPostGate")
                : Check(await agent.UpdatePostGate(gate, CancellationToken.None), "UpdatePostGate");
        }
        catch (Exception ex)
        {
            LogService.Warn("Gates", $"Writing the post gate threw: {ex.Message}");
            return false;
        }
    }

    private static bool Check<T>(AtProtoHttpResult<T> result, string call)
    {
        if (result.Succeeded)
            return true;

        LogService.Warn("Gates", $"{call} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        return false;
    }

    /// <summary>A post with no gate record answers like a missing record; that means "default", not an error.</summary>
    private static bool IsMissing(System.Net.HttpStatusCode status) => (int)status is 400 or 404;

    internal static ICollection<ThreadGateRule> ToThreadRules(IReadOnlyList<GateRule> rules)
    {
        List<ThreadGateRule> result = [];
        foreach (GateRule rule in rules)
        {
            switch (rule.Kind)
            {
                case GateRuleKind.Mentioned:
                    result.Add(new MentionRule());
                    break;
                case GateRuleKind.Followers:
                    result.Add(new FollowerRule());
                    break;
                case GateRuleKind.Following:
                    result.Add(new FollowingRule());
                    break;
                case GateRuleKind.List when !string.IsNullOrEmpty(rule.ListUri):
                    result.Add(new ListRule(new AtUri(rule.ListUri)));
                    break;
            }
        }
        return result;
    }

    /// <summary>A gate whose rules list is missing allows everyone; an empty list allows no one.</summary>
    internal static IReadOnlyList<GateRule>? FromThreadRules(IEnumerable<ThreadGateRule>? rules)
    {
        if (rules is null)
            return null;

        List<GateRule> result = [];
        foreach (ThreadGateRule rule in rules)
        {
            switch (rule)
            {
                case MentionRule:
                    result.Add(new GateRule(GateRuleKind.Mentioned));
                    break;
                case FollowerRule:
                    result.Add(new GateRule(GateRuleKind.Followers));
                    break;
                case FollowingRule:
                    result.Add(new GateRule(GateRuleKind.Following));
                    break;
                case ListRule list:
                    result.Add(new GateRule(GateRuleKind.List, list.List.ToString()));
                    break;
            }
        }
        return result;
    }
}
