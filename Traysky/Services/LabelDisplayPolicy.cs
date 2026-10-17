using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>How loudly a label wants to be shown. Mirrors the lexicon's "alert" / "inform" / "none".</summary>
public enum LabelSeverity
{
    None,
    Inform,
    Alert
}

/// <summary>One label as the wire carries it, stripped of library types.</summary>
public sealed record LabelInput(string Source, string Value, bool IsNegation, DateTimeOffset Created);

/// <summary>What a labeler (or the protocol, for the global labels) says a label value means.</summary>
public sealed record LabelText(string Name, string Description, LabelSeverity Severity, bool IgnoredByDefault = false);

/// <summary>A label ready to show: resolved name and description, and who applied it.</summary>
public sealed record LabelDisplay(
    string Value,
    string Name,
    string Description,
    LabelSeverity Severity,
    string SourceName,
    bool IsSelfApplied)
{
    public bool HasDescription => Description.Length > 0;

    public bool IsAlert => Severity == LabelSeverity.Alert;

    /// <summary>"Added by the account" / "Labeled by Some Labeler" - empty when the labeler is unknown.</summary>
    public string SourceLine => IsSelfApplied
        ? "Added by the account itself"
        : SourceName.Length > 0 ? "Labeled by " + SourceName : string.Empty;

    public bool HasSourceLine => SourceLine.Length > 0;
}

/// <summary>
/// Turns the raw labels on an account into the short list worth showing: negated labels are
/// dropped, duplicates collapsed, labels the user set to "ignore" left out, and each survivor
/// given a human name. No WinRT or library dependency, so it is unit tested.
/// </summary>
public static class LabelDisplayPolicy
{
    /// <summary>
    /// The labels to show for <paramref name="subjectDid"/>, alerts first.
    /// </summary>
    /// <param name="definitionFor">A labeler's own definition of (source, value), or null if it has none.</param>
    /// <param name="ignoredPreference">The user's "ignore" setting for (source, value): true/false, or null when they never chose.</param>
    /// <param name="sourceNameFor">A readable name for a labeler's DID; empty when unknown.</param>
    public static IReadOnlyList<LabelDisplay> Resolve(
        string subjectDid,
        IEnumerable<LabelInput>? labels,
        Func<string, string, LabelText?> definitionFor,
        Func<string, string, bool?> ignoredPreference,
        Func<string, string> sourceNameFor)
    {
        if (labels is null)
            return [];

        // The newest label per (source, value) wins, so a later negation cancels an earlier label.
        // First-seen order is kept for a stable list.
        Dictionary<(string Source, string Value), LabelInput> latest = [];
        List<(string Source, string Value)> order = [];
        foreach (LabelInput label in labels)
        {
            if (string.IsNullOrWhiteSpace(label.Value))
                continue;

            (string, string) key = (label.Source, label.Value);
            if (!latest.TryGetValue(key, out LabelInput? current))
                order.Add(key);

            if (current is null || label.Created >= current.Created)
                latest[key] = label;
        }

        List<LabelDisplay> shown = [];
        foreach ((string source, string value) in order)
        {
            if (latest[(source, value)].IsNegation)
                continue;

            LabelText text = definitionFor(source, value) ?? BuiltIn(value) ?? new LabelText(Humanize(value), string.Empty, LabelSeverity.Inform);

            if (ignoredPreference(source, value) ?? text.IgnoredByDefault)
                continue;

            bool self = string.Equals(source, subjectDid, StringComparison.Ordinal);
            shown.Add(new LabelDisplay(value, text.Name, text.Description, text.Severity, self ? string.Empty : sourceNameFor(source), self));
        }

        // Stable: alerts first, otherwise the order the labels came in.
        return shown.OrderByDescending(l => l.Severity).ToList();
    }

    /// <summary>
    /// The protocol's global label values, which no labeler defines. Wording follows what
    /// bsky.app shows for them.
    /// </summary>
    public static LabelText? BuiltIn(string value) => value switch
    {
        "porn" => new LabelText("Adult content", "Explicit sexual images.", LabelSeverity.Alert),
        "sexual" => new LabelText("Sexually suggestive", "Does not include nudity.", LabelSeverity.Alert),
        "nudity" => new LabelText("Non-sexual nudity", "E.g. artistic nudes.", LabelSeverity.Alert),
        "graphic-media" or "gore" => new LabelText("Graphic media", "Explicit or potentially disturbing media.", LabelSeverity.Alert),
        "!warn" => new LabelText("Content warning", "Moderators flagged this account or post for a warning.", LabelSeverity.Alert),
        "!hide" => new LabelText("Hidden by moderators", "Moderators asked for this to be hidden.", LabelSeverity.Alert),
        "!no-unauthenticated" => new LabelText("Hidden from signed-out users", "This account asked not to be shown to people who aren't signed in.", LabelSeverity.Inform),
        "!no-promote" => new LabelText("Not promoted", "Left out of discovery feeds.", LabelSeverity.Inform),
        _ => null
    };

    /// <summary>"dmca-violation" -> "Dmca violation": the fallback when nothing defines a value.</summary>
    public static string Humanize(string value)
    {
        string text = value.TrimStart('!').Replace('-', ' ').Replace('_', ' ').Trim();
        return text.Length == 0 ? value : char.ToUpperInvariant(text[0]) + text[1..];
    }

    public static LabelSeverity ParseSeverity(string? severity) => severity switch
    {
        "alert" => LabelSeverity.Alert,
        "inform" => LabelSeverity.Inform,
        _ => LabelSeverity.None
    };
}
