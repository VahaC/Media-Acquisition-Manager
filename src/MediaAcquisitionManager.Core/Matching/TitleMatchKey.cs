using System.Globalization;
using System.Text;

namespace MediaAcquisitionManager.Core.Matching;

/// <summary>
/// The form two titles are compared in when a search or a release is matched to a library work
/// (MAM-12): upper-cased, with punctuation and spacing reduced to single spaces, so "Spider-Man: No Way
/// Home" and "spider man no way home" are one title.
/// </summary>
/// <remarks>
/// <para>
/// Exact keys, never a distance: a weaker look-alike is not a match (ADR 0012). Upper-cased with
/// <see cref="string.ToUpperInvariant"/>, culture independent like <c>LibrarySearchText</c>, so a key
/// never depends on the machine that made it.
/// </para>
/// <para>
/// Letters, digits and combining marks are kept: a mark is part of the letter before it — a decomposed
/// "й" is "и" and a combining breve — and a word end there would cut the word in two. Everything else
/// (spaces, punctuation, symbols, brackets) is where one word ends and the next begins.
/// </para>
/// <para>
/// MAM-12 — except an apostrophe, which is dropped so the letters around it join: "Ocean's Eleven" is
/// "OCEANS ELEVEN". A scene name leaves it out ("Oceans.Eleven.2001"), so as a word break it would never
/// let the two match; and Ukrainian writes it inside a word three ways — "Дев'ять", "Дев’ять",
/// "Девʼять" — of which U+02BC is a letter (Lm) and the other two punctuation, so one title would get two
/// keys by whichever a source typed. Every character typed for one is in <see cref="Apostrophes"/>.
/// </para>
/// </remarks>
public static class TitleMatchKey
{
    /// <summary>
    /// The apostrophes a key drops (MAM-12): the typewriter one (U+0027), the left and right single
    /// quotes (U+2018, U+2019), the modifier letters apostrophe and turned comma (U+02BC, U+02BB), the
    /// grave and acute accents typed in their place (U+0060, U+00B4), and the full-width one (U+FF07).
    /// </summary>
    /// <remarks>
    /// The library match's SQL pre-filter removes the same characters from the stored copy of a title
    /// (<c>LibraryMatcher</c>), one <c>replace()</c> each: a character added here and not there would let
    /// the pre-filter leave out a title that matches. <c>SearchesLibraryMatchingHttpTests</c> stores a
    /// title with each of these and finds it.
    /// </remarks>
    public const string Apostrophes = "\u0027\u2018\u2019\u02BC\u02BB\u0060\u00B4\uFF07";

    /// <summary>
    /// <paramref name="title"/> upper-cased, its <see cref="Apostrophes"/> dropped, every run of other
    /// characters that are neither letters, digits nor combining marks replaced by one space, and trimmed:
    /// <c>"SPIDER MAN NO WAY HOME"</c>, <c>"OCEANS ELEVEN"</c>; <c>""</c> for a title with no letter or
    /// digit at all.
    /// </summary>
    public static string For(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var upper = title.ToUpperInvariant();
        var key = new StringBuilder(upper.Length);
        var separated = false;
        Span<char> utf16 = stackalloc char[2];
        // By rune, not by char: a letter outside the Basic Multilingual Plane is two chars, neither of
        // which is a letter on its own, and would split its word in two.
        foreach (var rune in upper.EnumerateRunes())
        {
            // Before the word test, and leaving a pending word break as it is: U+02BC is a letter, and
            // "Ocean 's" still has the space before it.
            if (IsApostrophe(rune))
                continue;
            if (!IsWordCharacter(rune))
            {
                separated = key.Length > 0;
                continue;
            }

            if (separated)
                key.Append(' ');
            separated = false;
            key.Append(utf16[..rune.EncodeToUtf16(utf16)]);
        }

        return key.ToString();
    }

    /// <summary>
    /// The longest word of <paramref name="key"/> (the first of the longest when several are as long),
    /// or <c>null</c> for an empty key.
    /// </summary>
    /// <remarks>
    /// The library match's SQL pre-filter: a title whose key equals the query's has every word of it in
    /// its stored <c>MediaWorkTitleEntity.NormalizedText</c> verbatim once that copy's
    /// <see cref="Apostrophes"/> are removed, because the copy is upper-cased by the same per-character
    /// <see cref="string.ToUpperInvariant"/> (<c>LibrarySearchText</c>) and a key, its apostrophes
    /// dropped, only replaces the characters between words. So asking whether <c>NormalizedText</c>, its
    /// apostrophes removed, contains the word never leaves out a title that matches, and the longest word
    /// leaves out the most that don't.
    /// </remarks>
    public static string? LongestWord(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string? longest = null;
        foreach (var word in key.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (longest is null || word.Length > longest.Length)
                longest = word;
        }

        return longest;
    }

    private static bool IsApostrophe(Rune rune) =>
        rune.IsBmp && Apostrophes.Contains((char)rune.Value, StringComparison.Ordinal);

    private static bool IsWordCharacter(Rune rune) =>
        Rune.IsLetterOrDigit(rune)
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}
