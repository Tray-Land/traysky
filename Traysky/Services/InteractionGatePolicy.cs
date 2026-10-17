using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>Who may reply to a thread. Everyone is "no thread gate"; Nobody is a gate with no rules.</summary>
public enum ReplyAudience
{
    Everyone,
    Nobody,
    Limited
}

public enum GateRuleKind
{
    Mentioned,
    Followers,
    Following,
    List
}

/// <summary>One allow rule of a thread gate, free of the idunno types so it can be tested.</summary>
public readonly record struct GateRule(GateRuleKind Kind, string? ListUri = null);

/// <summary>What the reply-controls UI edits: an audience, and for <see cref="ReplyAudience.Limited"/> which groups are let in.</summary>
public sealed record ReplyChoice(
    ReplyAudience Audience,
    bool Mentioned = false,
    bool Followers = false,
    bool Following = false,
    IReadOnlyList<string>? ListUris = null)
{
    public static ReplyChoice Everyone { get; } = new(ReplyAudience.Everyone);

    public static ReplyChoice Nobody { get; } = new(ReplyAudience.Nobody);

    public IReadOnlyList<string> Lists => ListUris ?? [];
}

/// <summary>
/// Reply controls (thread gates) and quote controls (post gates). Maps what the UI offers to the
/// rules the records carry and back, and decides where the controls apply. Pure .NET on purpose.
/// </summary>
public static class InteractionGatePolicy
{
    /// <summary>The lexicon allows at most five allow rules on a thread gate.</summary>
    public const int MaxRules = 5;

    /// <summary>
    /// The rules to write for a choice: null for "everyone" (no gate record at all), an empty list for
    /// "nobody". A limited choice with nothing ticked means nobody, which is what bsky.app does too.
    /// </summary>
    public static IReadOnlyList<GateRule>? ToRules(ReplyChoice choice)
    {
        switch (choice.Audience)
        {
            case ReplyAudience.Everyone:
                return null;
            case ReplyAudience.Nobody:
                return [];
        }

        List<GateRule> rules = [];
        if (choice.Mentioned)
            rules.Add(new GateRule(GateRuleKind.Mentioned));
        if (choice.Followers)
            rules.Add(new GateRule(GateRuleKind.Followers));
        if (choice.Following)
            rules.Add(new GateRule(GateRuleKind.Following));

        foreach (string list in choice.Lists.Where(l => !string.IsNullOrEmpty(l)).Distinct(StringComparer.Ordinal))
            rules.Add(new GateRule(GateRuleKind.List, list));

        return rules.Take(MaxRules).ToList();
    }

    /// <summary>The inverse of <see cref="ToRules"/>: null is everyone, empty is nobody, anything else is limited.</summary>
    public static ReplyChoice FromRules(IReadOnlyList<GateRule>? rules)
    {
        if (rules is null)
            return ReplyChoice.Everyone;
        if (rules.Count == 0)
            return ReplyChoice.Nobody;

        return new ReplyChoice(
            ReplyAudience.Limited,
            Mentioned: rules.Any(r => r.Kind == GateRuleKind.Mentioned),
            Followers: rules.Any(r => r.Kind == GateRuleKind.Followers),
            Following: rules.Any(r => r.Kind == GateRuleKind.Following),
            ListUris: rules.Where(r => r.Kind == GateRuleKind.List && !string.IsNullOrEmpty(r.ListUri)).Select(r => r.ListUri!).ToList());
    }

    /// <summary>Collapses a limited choice that lets no one in to "nobody", so what the dialog shows matches what is stored.</summary>
    public static ReplyChoice Normalize(ReplyChoice choice) => FromRules(ToRules(choice));

    /// <summary>Whether two choices store the same rules, ignoring the order lists were ticked in.</summary>
    public static bool AreSame(ReplyChoice a, ReplyChoice b)
    {
        a = Normalize(a);
        b = Normalize(b);
        return a.Audience == b.Audience
            && a.Mentioned == b.Mentioned
            && a.Followers == b.Followers
            && a.Following == b.Following
            && a.Lists.ToHashSet(StringComparer.Ordinal).SetEquals(b.Lists);
    }

    /// <summary>Whether one more list can be ticked without going past <see cref="MaxRules"/>.</summary>
    public static bool CanTickList(ReplyChoice choice)
    {
        int used = (choice.Mentioned ? 1 : 0) + (choice.Followers ? 1 : 0) + (choice.Following ? 1 : 0) + choice.Lists.Count;
        return used < MaxRules;
    }

    /// <summary>Whether writing this choice needs a thread gate record (everyone is the absence of one).</summary>
    public static bool NeedsThreadGate(ReplyChoice choice) => choice.Audience != ReplyAudience.Everyone;

    /// <summary>Quotes are on by default; turning them off is a post gate with the "disable embedding" rule.</summary>
    public static bool NeedsPostGate(bool allowQuotes) => !allowQuotes;

    /// <summary>Reply controls live on the thread's root post, so a reply has none to offer.</summary>
    public static bool ShowsReplyControl(bool isReply) => !isReply;

    /// <summary>Own posts only; the gate's record key is the thread root's, so it can't be set on a reply.</summary>
    public static bool CanEditReplySettings(bool own, bool isThreadRoot) => own && isThreadRoot;

    public static bool CanEditQuoteSettings(bool own) => own;

    /// <summary>Whether the choice and quote setting are the defaults, so the compose box need not draw attention to them.</summary>
    public static bool IsDefault(ReplyChoice choice, bool allowQuotes) =>
        choice.Audience == ReplyAudience.Everyone && allowQuotes;

    public static string ReplySummary(ReplyChoice choice)
    {
        switch (choice.Audience)
        {
            case ReplyAudience.Everyone:
                return "Anyone can reply";
            case ReplyAudience.Nobody:
                return "Replies are off";
        }

        List<string> who = [];
        if (choice.Mentioned)
            who.Add("people mentioned");
        if (choice.Followers)
            who.Add("followers");
        if (choice.Following)
            who.Add("people you follow");
        if (choice.Lists.Count == 1)
            who.Add("one list");
        else if (choice.Lists.Count > 1)
            who.Add($"{choice.Lists.Count} lists");

        return who.Count == 0 ? "Replies are off" : $"Replies from {Join(who)}";
    }

    public static string QuoteSummary(bool allowQuotes) => allowQuotes ? "Quotes allowed" : "Quotes are off";

    /// <summary>The compose box's one-line status, e.g. "Replies from followers · Quotes are off".</summary>
    public static string Summary(ReplyChoice choice, bool allowQuotes, bool isReply) =>
        isReply ? QuoteSummary(allowQuotes) : $"{ReplySummary(choice)} · {QuoteSummary(allowQuotes)}";

    private static string Join(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
    };
}
