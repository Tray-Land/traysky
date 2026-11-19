using System;
using System.Collections.Generic;
using System.Linq;

namespace Traysky.Services;

/// <summary>
/// The languages a post is tagged with (the record's <c>langs</c>), as bsky.app's composer offers.
/// Pure so it can be tested: the lexicon allows at most three tags, and the choice persists between
/// posts, falling back to the app's UI language until one is made.
/// </summary>
public static class ComposeLanguagePolicy
{
    /// <summary>The <c>app.bsky.feed.post</c> lexicon caps <c>langs</c> at three.</summary>
    public const int MaxLanguages = 3;

    public readonly record struct Language(string Code, string Name);

    public static IReadOnlyList<Language> All { get; } =
    [
        new("en", "English"),
        new("es", "Spanish"),
        new("fr", "French"),
        new("de", "German"),
        new("pt", "Portuguese"),
        new("it", "Italian"),
        new("nl", "Dutch"),
        new("sv", "Swedish"),
        new("da", "Danish"),
        new("nb", "Norwegian"),
        new("fi", "Finnish"),
        new("pl", "Polish"),
        new("cs", "Czech"),
        new("hu", "Hungarian"),
        new("ro", "Romanian"),
        new("el", "Greek"),
        new("tr", "Turkish"),
        new("ru", "Russian"),
        new("uk", "Ukrainian"),
        new("he", "Hebrew"),
        new("ar", "Arabic"),
        new("fa", "Persian"),
        new("hi", "Hindi"),
        new("bn", "Bengali"),
        new("th", "Thai"),
        new("vi", "Vietnamese"),
        new("id", "Indonesian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("zh", "Chinese"),
    ];

    public static bool IsKnown(string? code) =>
        code is not null && All.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    public static string NameOf(string code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)).Name ?? code;

    /// <summary>Parses the stored comma-separated codes, dropping unknown, blank and repeated ones and anything past the cap.</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        Clean((stored ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    public static string Serialize(IEnumerable<string> codes) => string.Join(',', Clean(codes));

    /// <summary>What to tag a post with: the saved choice, else the UI language, else English.</summary>
    public static IReadOnlyList<string> Resolve(string? stored, string? uiLanguageCode)
    {
        IReadOnlyList<string> saved = Parse(stored);
        if (saved.Count > 0)
            return saved;

        return IsKnown(uiLanguageCode) ? [uiLanguageCode!.ToLowerInvariant()] : ["en"];
    }

    /// <summary>Ticks or unticks <paramref name="code"/>. Ticking a fourth is refused (the list comes back unchanged), and the last one can't be unticked: a post always carries a language.</summary>
    public static IReadOnlyList<string> Toggle(IReadOnlyList<string> selected, string code)
    {
        if (!IsKnown(code))
            return selected;

        string lower = code.ToLowerInvariant();
        if (selected.Contains(lower))
            return selected.Count <= 1 ? selected : selected.Where(c => c != lower).ToList();

        return selected.Count >= MaxLanguages ? selected : [.. selected, lower];
    }

    public static bool CanTick(IReadOnlyList<string> selected, string code) =>
        selected.Contains(code.ToLowerInvariant()) || selected.Count < MaxLanguages;

    /// <summary>The short label for the compose box's language button: "EN", or "EN, ES" for several.</summary>
    public static string Label(IReadOnlyList<string> selected) =>
        selected.Count == 0 ? "EN" : string.Join(", ", selected.Select(c => c.ToUpperInvariant()));

    /// <summary>"English" or "English, Spanish": the button's tooltip.</summary>
    public static string Description(IReadOnlyList<string> selected) =>
        selected.Count == 0 ? "English" : string.Join(", ", selected.Select(NameOf));

    private static IReadOnlyList<string> Clean(IEnumerable<string> codes) =>
        codes.Select(c => c.Trim().ToLowerInvariant())
            .Where(IsKnown)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxLanguages)
            .ToList();
}
