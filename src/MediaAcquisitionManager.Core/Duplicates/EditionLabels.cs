using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — the editions a file's name or a library's version name says a copy is ("Aliens.1986.Special.Edition.mkv",
/// a version named "Director's Cut"): a short word list, English and Ukrainian (Vasyl, 2026-10-07).
/// </summary>
/// <remarks>
/// <para>
/// ADR 0012 rejected a list of edition words for <em>matching</em> — the list never ends, and uploaders write them in
/// Ukrainian too — and that still holds: this list decides no match and keeps no two works apart. It only labels a
/// possible distinction between copies already grouped by their ids or by their title and year (specification §3.3), so
/// a word it misses costs a flag, never a wrong group (ADR 0028). A word of the work's own title is no label
/// (<see cref="Read(string?, IEnumerable{string})"/>).
/// </para>
/// <para>
/// A label is a whole word or phrase, between characters that are neither letters nor digits (the
/// <c>EpisodeMarker</c> boundary), in any letter case: "extendedly" and "theatricals" say nothing. Between the words of a
/// phrase any run of spaces, dots, underscores or dashes — the ways file names separate words — and the apostrophe of
/// "Director's" may be typed three ways or left out.
/// </para>
/// </remarks>
public static partial class EditionLabels
{
    /// <summary>Every edition and the pattern that says it, in <see cref="MediaEdition"/>'s order.</summary>
    private static readonly (MediaEdition Edition, Regex Pattern)[] Patterns =
    [
        (MediaEdition.Theatrical, TheatricalPattern()),
        (MediaEdition.DirectorsCut, DirectorsCutPattern()),
        (MediaEdition.Extended, ExtendedPattern()),
        (MediaEdition.Unrated, UnratedPattern()),
        (MediaEdition.Uncut, UncutPattern()),
        (MediaEdition.Remastered, RemasteredPattern()),
        (MediaEdition.SpecialEdition, SpecialEditionPattern()),
        (MediaEdition.Imax, ImaxPattern()),
        (MediaEdition.OpenMatte, OpenMattePattern()),
        (MediaEdition.FinalCut, FinalCutPattern()),
        (MediaEdition.Redux, ReduxPattern()),
        (MediaEdition.International, InternationalPattern()),
        (MediaEdition.Alternate, AlternatePattern()),
        (MediaEdition.Assembly, AssemblyPattern()),
        (MediaEdition.Widescreen, WidescreenPattern()),
        (MediaEdition.FullScreen, FullScreenPattern()),
    ];

    /// <summary>
    /// The editions <paramref name="text"/> names, each once, in <see cref="MediaEdition"/>'s order; none for a blank
    /// text or one that names none. "Unrated Director's Cut" is both <see cref="MediaEdition.Unrated"/> and
    /// <see cref="MediaEdition.DirectorsCut"/>.
    /// </summary>
    public static IReadOnlyList<MediaEdition> Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return [.. Patterns.Where(pattern => pattern.Pattern.IsMatch(text)).Select(pattern => pattern.Edition)];
    }

    /// <summary>
    /// The editions <paramref name="text"/> names besides the title of its work: each of <paramref name="titles"/> that
    /// names an edition itself — "Uncut Gems", "The Final Cut", "Redux Riding Hood" — is taken out of the text first, as
    /// a whole phrase in any letter case with any characters but letters and digits between its words, so
    /// "Uncut.Gems.2019.1080p.mkv" names no edition. A title that names none is left as it is.
    /// </summary>
    /// <remarks>
    /// MAM-20 — a title is no label of a copy: read whole, one library's English file name and another's localized one
    /// flagged two copies of one cut as two editions. The cost: a work whose every title names the edition of a copy
    /// ("Apocalypse Now Redux" as the title) loses that label from the copy's name (ADR 0028).
    /// </remarks>
    public static IReadOnlyList<MediaEdition> Read(string? text, IEnumerable<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // The longest first: "Blade Runner: The Final Cut" goes whole before "The Final Cut" could take its end.
        foreach (var title in titles.Where(title => Read(title).Count > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(title => title.Length))
            text = TitlePattern(title).Replace(text, " ");
        return Read(text);
    }

    // A title as a whole phrase: its words in order, any run of what is neither a letter nor a digit between them, in
    // any letter case. Built only for the rare title that names an edition; the words are escaped, so nothing a library
    // writes is read as a pattern.
    private static Regex TitlePattern(string title) =>
        new(
            @"(?<![\p{L}\p{N}])"
                + string.Join(@"[^\p{L}\p{N}]+", WordPattern().Matches(title).Select(word => Regex.Escape(word.Value)))
                + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    // Each pattern sits between (?<![\p{L}\p{N}]) and (?![\p{L}\p{N}]), so it is a whole word; [\s._-]+ is the
    // separator between the words of a phrase.

    // The cinema cut: the bare word too ("Aliens.Theatrical.mkv"), and the three ways Ukrainian releases write it.
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:theatrical(?:[\s._-]+(?:cut|edition|version))?|(?:кіно)?театральна[\s._-]+версія|прокатна[\s._-]+версія)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TheatricalPattern();

    // "Director's Cut", "Directors Cut", "Director’s cut", "Director Cut", "Director's Definitive Cut"; Ukrainian writes
    // "режисерська версія", or the adjective alone.
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:director(?:['’ʼ]?s)?[\s._-]+(?:definitive[\s._-]+)?cut|режисерська(?:[\s._-]+версія)?)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectorsCutPattern();

    // The bare word too: "Extended" alone in a name is the longer cut.
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:extended(?:[\s._-]+(?:cut|edition|version))?|розширена(?:[\s._-]+версія)?)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExtendedPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])unrated(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnratedPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:uncut|без[\s._-]+цензури)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UncutPattern();

    // "Remaster" and "Remastered"; Ukrainian trackers write "ремастер" and "ремастеринг".
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:remaster(?:ed)?|ремастер(?:инг)?)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemasteredPattern();

    // Two words: "Special" alone is a title's word ("Special Forces").
    [GeneratedRegex(@"(?<![\p{L}\p{N}])special[\s._-]+edition(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpecialEditionPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])imax(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImaxPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])open[\s._-]+matte(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpenMattePattern();

    // Two words: "Final" alone is a title's word ("Final Destination").
    [GeneratedRegex(@"(?<![\p{L}\p{N}])final[\s._-]+cut(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FinalCutPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])redux(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReduxPattern();

    // Two words: "International" alone is a title's word ("The International").
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])international[\s._-]+(?:cut|version)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternationalPattern();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])alternat(?:e|ive)[\s._-]+(?:cut|version)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlternatePattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])assembly[\s._-]+cut(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])widescreen(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WidescreenPattern();

    // "Fullscreen", "Full Screen", "Full.Screen".
    [GeneratedRegex(@"(?<![\p{L}\p{N}])full(?:[\s._-]+)?screen(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FullScreenPattern();
}
