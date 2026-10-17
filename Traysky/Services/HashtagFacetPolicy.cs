using System;
using System.Collections.Generic;
using System.Text;

namespace Traysky.Services;

/// <summary>One '#tag' found in post text: its UTF-8 byte range (what a Bluesky facet indexes) and the tag without the '#'.</summary>
public readonly record struct HashtagSpan(int ByteStart, int ByteEnd, string Tag);

/// <summary>
/// Finds hashtags in post text the way Bluesky does: '#' at the start or after whitespace, then
/// everything up to the next whitespace, so interior punctuation such as the hyphens in
/// "#tray-tober" stays part of the one tag. Trailing punctuation ("#tag.", "#tag!") is not.
/// </summary>
public static class HashtagFacetPolicy
{
    public const int MaxTagLength = 64;

    public static IReadOnlyList<HashtagSpan> Find(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<HashtagSpan> spans = [];
        int i = 0;
        while (i < text.Length)
        {
            if (!IsHash(text[i]) || (i > 0 && !char.IsWhiteSpace(text[i - 1])))
            {
                i++;
                continue;
            }

            int end = i + 1;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
                end++;

            while (end > i + 1 && IsTrailingPunctuation(text[end - 1]))
                end--;

            string tag = text[(i + 1)..end];
            if (IsValidTag(tag))
            {
                int byteStart = Encoding.UTF8.GetByteCount(text.AsSpan(0, i));
                int byteEnd = byteStart + Encoding.UTF8.GetByteCount(text.AsSpan(i, end - i));
                spans.Add(new HashtagSpan(byteStart, byteEnd, tag));
            }

            i = Math.Max(end, i + 1);
        }

        return spans;
    }

    /// <summary>
    /// Whether the caret, at the end of <paramref name="textBeforeCaret"/>, is inside a tag being
    /// typed: a '#' that starts the text or follows whitespace (or a token's zero-width-space
    /// padding), then no whitespace. <paramref name="start"/> is the '#''s index; <paramref name="query"/>
    /// is what follows it, hyphens included. RichSuggestBox can't do this itself: it finds its
    /// query by stepping back whole RichEdit "words", and a hyphen ends one.
    /// </summary>
    public static bool TryFindOpenTag(string? textBeforeCaret, out int start, out string query)
    {
        start = 0;
        query = string.Empty;
        if (string.IsNullOrEmpty(textBeforeCaret))
            return false;

        int i = textBeforeCaret.Length;
        while (i > 0 && !char.IsWhiteSpace(textBeforeCaret[i - 1]) && textBeforeCaret[i - 1] != '​')
            i--;

        if (i >= textBeforeCaret.Length || !IsHash(textBeforeCaret[i]))
            return false;

        string typed = textBeforeCaret[(i + 1)..];
        if (typed.Length > MaxTagLength || typed.Contains('#'))
            return false;

        start = i;
        query = typed;
        return true;
    }

    private static bool IsHash(char c) => c is '#' or '＃';

    private static bool IsValidTag(string tag)
    {
        if (tag.Length == 0 || tag.Length > MaxTagLength)
            return false;

        // A bare number ("#1") is a ranking, not a tag.
        foreach (char c in tag)
        {
            if (!char.IsDigit(c))
                return true;
        }

        return false;
    }

    private static bool IsTrailingPunctuation(char c) => char.IsPunctuation(c) || char.IsSymbol(c);
}
