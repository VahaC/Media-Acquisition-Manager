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
