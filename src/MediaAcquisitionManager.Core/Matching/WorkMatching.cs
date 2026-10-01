using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Matching;

/// <summary>
/// Decides whether a work is the one a search or a release claims, and on what evidence (MAM-12,
/// specification §3.1's priorities): a reliable identifier, then the original or localized title with
/// the year, then the title alone as a candidate for manual review.
/// </summary>
/// <remarks>
/// <para>
/// Every match is evidence to list, strongest first, never a decision: nothing is picked, merged or used
/// to act on (ADR 0012). A conflict is flagged and never resolved — a work two sources give two IMDb
/// ids still matches either, and says so.
/// </para>
/// <para>
/// Identifiers compare by scheme and by value trimmed, in any letter case (<c>tt0090605</c> is
/// <c>TT0090605</c>), the <see cref="Library.LibraryWorkGrouping"/> rule; a blank value names nothing.
/// Titles compare by <see cref="TitleMatchKey"/> and years exactly. Nothing else is looked at — no
/// distance between titles, no year ± 1: a weaker look-alike is not a match.
/// </para>
/// <para>
/// MAM-12 — <see cref="MatchByWords"/> lists, below the matches, a work holding every word of a title
/// sought; never a match, and never used for a release (ADR 0012).
/// </para>
/// </remarks>
public static class WorkMatching
{
    /// <summary>
    /// How the work with <paramref name="titles"/> and <paramref name="identifiers"/> — every title and id
    /// any source gave it — matches <paramref name="claim"/>; <c>null</c> when it doesn't.
    /// </summary>
    public static WorkMatch? Match(WorkClaim claim, IReadOnlyList<ReportedTitle> titles, IReadOnlyList<MediaIdentifier> identifiers)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(identifiers);

        var claimed = claim.Identifiers.Select(Key).OfType<(MediaIdentifierScheme, string)>().ToHashSet();
        var matchedIdentifiers = identifiers
            .Where(identifier => Key(identifier) is { } key && claimed.Contains(key))
            .Distinct()
            .ToList();

        var claimedKeys = claim.Titles.Select(TitleMatchKey.For).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        var matchedTitles = titles.Where(title => claimedKeys.Contains(TitleMatchKey.For(title.Text))).ToList();
        var matchedWithYear = claim.Year is { } year ? matchedTitles.Where(title => title.Year == year).ToList() : [];

        var conflicts = ConflictingSchemes(claim.Identifiers, identifiers);
        if (matchedIdentifiers.Count > 0)
            return new WorkMatch(MatchEvidence.Identifier, matchedIdentifiers, Preferred(matchedWithYear) ?? Preferred(matchedTitles), conflicts);
        if (matchedWithYear.Count > 0)
            return new WorkMatch(MatchEvidence.TitleAndYear, [], Preferred(matchedWithYear), conflicts);
        if (matchedTitles.Count > 0)
            return new WorkMatch(MatchEvidence.TitleOnly, [], Preferred(matchedTitles), conflicts);
        return null;
    }

    /// <summary>
    /// MAM-12 — <see cref="MatchEvidence.Words"/> for the work with <paramref name="titles"/>,
    /// <paramref name="filePaths"/> (its files' paths as the services report them) and
    /// <paramref name="identifiers"/> when one of its titles, of any source and either kind, or one of its
    /// paths holds every word of a title of <paramref name="claim"/> (<see cref="TitleMatchKey.Words"/>),
    /// each as a whole word and in any order; <c>null</c> otherwise. No matched ids; as the matched title,
    /// the one holding the words — a displayed one before an original one, then in the work's order — or
    /// <c>null</c> when only a path holds them. For a work <see cref="Match"/> doesn't match: the caller
    /// asks <c>Match(…) ?? MatchByWords(…)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never a match (ADR 0012): the search page's weakest listing, so a person who searched "Rambo" sees
    /// "Rambo III" and the "First Blood" in the "Rambo. Collection" folder, and a look-alike ("Interstellar
    /// Wars" for "Interstellar") is shown for what it is. A release is never matched this way — its
    /// claim is read from a name that may carry any word at all.
    /// </para>
    /// <para>
    /// Whole words, so "Alien" is not in "Aliens"; and in one title or one path, so a displayed title and an
    /// original one holding half the words each don't list the work. The year and the ids play no part in it — the ids sought are
    /// <see cref="MatchEvidence.Identifier"/>'s — but a conflict is flagged as <see cref="Match"/> flags
    /// it. A title whose key is empty seeks nothing.
    /// </para>
    /// </remarks>
    public static WorkMatch? MatchByWords(
        WorkClaim claim, IReadOnlyList<ReportedTitle> titles, IReadOnlyList<string> filePaths, IReadOnlyList<MediaIdentifier> identifiers)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentNullException.ThrowIfNull(identifiers);

        var sought = claim.Titles
            .Select(title => TitleMatchKey.Words(TitleMatchKey.For(title)))
            .Where(words => words.Count > 0)
            .ToList();
        if (sought.Count == 0)
            return null;

        var holdingTitles = titles.Where(title => Holds(title.Text)).ToList();
        if (holdingTitles.Count == 0 && !filePaths.Any(Holds))
            return null;
        return new WorkMatch(MatchEvidence.Words, [], Preferred(holdingTitles), ConflictingSchemes(claim.Identifiers, identifiers));

        bool Holds(string text)
        {
            var words = TitleMatchKey.Words(TitleMatchKey.For(text)).ToHashSet(StringComparer.Ordinal);
            return sought.Any(title => title.All(words.Contains));
        }
    }

    /// <summary>
    /// The schemes, in scheme order, the claim and the work hold values of but share none of (the search
    /// asked for tt0816692, the work holds tt9999999), and those the work holds two values of itself.
    /// </summary>
    private static List<MediaIdentifierScheme> ConflictingSchemes(IReadOnlyList<MediaIdentifier> claimed, IReadOnlyList<MediaIdentifier> held)
    {
        var claimedValues = ValuesByScheme(claimed);
        var heldValues = ValuesByScheme(held);
        return heldValues
            .Where(scheme => scheme.Value.Count > 1
                || (claimedValues.TryGetValue(scheme.Key, out var sought) && !sought.Overlaps(scheme.Value)))
            .Select(scheme => scheme.Key)
            .Order()
            .ToList();
    }

    private static Dictionary<MediaIdentifierScheme, HashSet<string>> ValuesByScheme(IReadOnlyList<MediaIdentifier> identifiers) =>
        identifiers
            .Select(Key)
            .OfType<(MediaIdentifierScheme Scheme, string Value)>()
            .GroupBy(key => key.Scheme)
            .ToDictionary(scheme => scheme.Key, scheme => scheme.Select(key => key.Value).ToHashSet(StringComparer.Ordinal));

    /// <summary>The form an id compares in: trimmed and upper-cased; <c>null</c> for a blank value.</summary>
    private static (MediaIdentifierScheme Scheme, string Value)? Key(MediaIdentifier identifier)
    {
        var value = identifier.Value.Trim().ToUpperInvariant();
        return value.Length == 0 ? null : (identifier.Scheme, value);
    }

    /// <summary>
    /// The title to show as the one that matched: a displayed title before an original one, then the
    /// work's own order of titles — so the same work and claim always show the same title.
    /// </summary>
    private static ReportedTitle? Preferred(List<ReportedTitle> titles) =>
        titles
            .OrderBy(title => title.Kind switch
            {
                MediaTitleKind.Display => 0,
                MediaTitleKind.Original => 1,
                _ => 2,
            })
            .FirstOrDefault();
}
