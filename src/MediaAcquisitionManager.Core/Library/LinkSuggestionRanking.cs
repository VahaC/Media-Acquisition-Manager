using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-34 — the evidence for each TMDb candidate of a library work, the order they are suggested in, and which one — if
/// any — is confident enough for "Accept all confident". Pure: computed whenever the suggestions are read (decision 7),
/// so a duration measured later, or a neighbour linked later, changes them without asking the catalog again.
/// </summary>
/// <remarks>
/// <para>
/// The order, every step deterministic: a candidate related to a linked neighbour first (a Tom &amp; Jerry short beside
/// others of the same director); then an exact title; the same year, then a year off by one (TMDb dates a film by its
/// first release, a library often by another), then any other or unknown year; the kind the work is; a runtime that
/// agrees, then an unknown one, then one that doesn't; the library's genre likewise; the closer title; the catalog's own
/// order; and kind and id last, so two runs never order a tie differently.
/// </para>
/// <para>
/// Confident (decision 8) is not "first in the order": the candidate must be of the work's kind, the <b>only</b>
/// candidate whose title has the key of one of the work's and whose year is the work's, and its runtime must agree with a
/// duration the app measured itself — both known. Two films of one title and year are two answers, so neither is; a show
/// suggested for a film is an episode-to-show link, which is never confident. A confident candidate is a link the owner
/// accepts without looking at it, so every doubt keeps it in the review.
/// </para>
/// </remarks>
public static class LinkSuggestionRanking
{
    /// <summary>
    /// The least two runtimes may differ by and still agree, in minutes: TMDb rounds a runtime to whole minutes, and a
    /// short's credits or a channel logo cut or added shift a file by a minute or two either way.
    /// </summary>
    public const int RuntimeToleranceMinimumMinutes = 2;

    /// <summary>
    /// The share of the catalog's runtime two runtimes may differ by and still agree, when that is more than
    /// <see cref="RuntimeToleranceMinimumMinutes"/>: a film's other cut (a director's, a TV one) or another frame rate
    /// (PAL's 4 % speed-up) moves a two-hour film by several minutes, while another film of the same title is rarely
    /// within a tenth of it.
    /// </summary>
    public const double RuntimeToleranceShare = 0.10;

    /// <summary>
    /// <paramref name="candidates"/> in the order they are suggested to the owner, each with its evidence and whether it
    /// is the confident one (at most one is).
    /// </summary>
    public static IReadOnlyList<RankedCandidate> Rank(LinkSuggestionSubject subject, IReadOnlyList<LinkCandidateFacts> candidates)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(candidates);

        var subjectKeys = subject.Titles
            .Select(TitleMatchKey.For)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var evidenced = candidates.Select(candidate => (Candidate: candidate, Evidence: EvidenceOf(subject, subjectKeys, candidate))).ToList();

        // Decision 8: the one candidate of an exact title and the same year, whatever its kind — two such answers are
        // a doubt the owner settles.
        var exactAndSameYear = evidenced.Where(item => item.Evidence.Title.Exact && item.Evidence.Year.Difference == 0).ToList();
        var confident = exactAndSameYear.Count == 1
            && exactAndSameYear[0].Evidence.KindAgrees
            && exactAndSameYear[0].Evidence.Runtime.Agrees == true
                ? exactAndSameYear[0].Candidate
                : null;

        return
        [
            .. evidenced
                .OrderByDescending(item => item.Evidence.Sibling is not null)
                .ThenByDescending(item => item.Evidence.Title.Exact)
                .ThenBy(item => YearRank(item.Evidence.Year))
                .ThenByDescending(item => item.Evidence.KindAgrees)
                .ThenBy(item => AgreementRank(item.Evidence.Runtime.Agrees))
                .ThenBy(item => GenreRank(item.Evidence.Genre.Agreement))
                .ThenByDescending(item => item.Evidence.Title.Similarity)
                .ThenBy(item => item.Candidate.Position)
                .ThenBy(item => item.Candidate.Kind)
                .ThenBy(item => item.Candidate.Id, StringComparer.Ordinal)
                .Select(item => new RankedCandidate(item.Candidate, item.Evidence, ReferenceEquals(item.Candidate, confident))),
        ];
    }

    /// <summary>Whether a measured duration and a catalog runtime agree, within the tolerance above.</summary>
    public static bool RuntimesAgree(int libraryMinutes, int catalogMinutes) =>
        Math.Abs(libraryMinutes - catalogMinutes)
        <= Math.Max(RuntimeToleranceMinimumMinutes, (int)Math.Round(RuntimeToleranceShare * catalogMinutes, MidpointRounding.AwayFromZero));

    // ── the evidence ─────────────────────────────────────────────────────────

    private static LinkEvidence EvidenceOf(LinkSuggestionSubject subject, HashSet<string> subjectKeys, LinkCandidateFacts candidate) =>
        new(
            TitleOf(subject, subjectKeys, candidate),
            YearOf(subject, candidate),
            RuntimeOf(subject, candidate),
            KindAgrees(subject.Kind, candidate.Kind),
            GenreOf(subject, candidate),
            SiblingOf(subject, candidate));

    // The best of every candidate title against every work title; on a tie, the catalog's title before its original
    // one before the others, each in the catalog's order.
    private static TitleEvidence TitleOf(LinkSuggestionSubject subject, HashSet<string> subjectKeys, LinkCandidateFacts candidate)
    {
        var best = new TitleEvidence(0, false, LinkTitleKind.Title, candidate.Title);
        foreach (var (kind, title) in TitlesOf(candidate))
        {
            var exact = subjectKeys.Contains(TitleMatchKey.For(title));
            var similarity = subject.Titles.Select(subjectTitle => TitleSimilarity.Of(subjectTitle, title)).DefaultIfEmpty(0).Max();
            if ((exact && !best.Exact) || (exact == best.Exact && similarity > best.Similarity))
                best = new TitleEvidence(similarity, exact, kind, title);
        }

        return best;
    }

    private static IEnumerable<(LinkTitleKind Kind, string Title)> TitlesOf(LinkCandidateFacts candidate)
    {
        yield return (LinkTitleKind.Title, candidate.Title);
        if (candidate.OriginalTitle is { } original)
            yield return (LinkTitleKind.OriginalTitle, original);
        foreach (var alternative in candidate.AlternativeTitles)
            yield return (LinkTitleKind.AlternativeTitle, alternative);
    }

    private static YearEvidence YearOf(LinkSuggestionSubject subject, LinkCandidateFacts candidate) =>
        new(subject.Year, candidate.Year, subject.Year is { } library && candidate.Year is { } catalog ? Math.Abs(library - catalog) : null);

    // A runtime of 0 is TMDb's "not known", and a duration of 0 no measurement; neither is compared.
    private static RuntimeEvidence RuntimeOf(LinkSuggestionSubject subject, LinkCandidateFacts candidate)
    {
        var measured = subject.VersionMinutes.Where(minutes => minutes > 0).ToList();
        var catalog = candidate.RuntimeMinutes is > 0 ? candidate.RuntimeMinutes : null;
        if (measured.Count == 0)
            return new RuntimeEvidence(null, catalog, null);
        if (catalog is not { } catalogMinutes)
            return new RuntimeEvidence(measured[0], null, null);

        // The version nearest the catalog's runtime: a work held as a short cut and a long one agrees through either.
        var nearest = measured.OrderBy(minutes => Math.Abs(minutes - catalogMinutes)).ThenBy(minutes => minutes).First();
        return new RuntimeEvidence(nearest, catalogMinutes, RuntimesAgree(nearest, catalogMinutes));
    }

    private static bool KindAgrees(MediaWorkKind work, MediaWorkKind candidate) =>
        (work, candidate) is (MediaWorkKind.Movie, MediaWorkKind.Movie) or (MediaWorkKind.Series, MediaWorkKind.Series);

    // Only Animation and Documentary are a library's genre (LibraryGenreProfile); and a candidate whose genres aren't
    // known says nothing either way.
    private static GenreEvidence GenreOf(LinkSuggestionSubject subject, LinkCandidateFacts candidate)
    {
        if (subject.LibraryRole is not (CatalogGenreRole.Animation or CatalogGenreRole.Documentary) || candidate.GenreRoles.Count == 0)
            return new GenreEvidence(subject.LibraryRole, LinkGenreAgreement.Unknown);
        return new GenreEvidence(
            subject.LibraryRole,
            candidate.GenreRoles.Contains(subject.LibraryRole.Value) ? LinkGenreAgreement.Agrees : LinkGenreAgreement.Disagrees);
    }

    // The nearest neighbour sharing the candidate's collection, a director (a creator), or — the candidate a show — the
    // show it is linked to.
    private static SiblingEvidence? SiblingOf(LinkSuggestionSubject subject, LinkCandidateFacts candidate)
    {
        foreach (var sibling in subject.Siblings)
        {
            if (candidate.CollectionId is { } collection && string.Equals(sibling.CollectionId, collection, StringComparison.Ordinal))
                return new SiblingEvidence(LinkSiblingReason.SameCollection, sibling.Title);
            if (sibling.DirectorIds.Any(director => candidate.DirectorIds.Contains(director, StringComparer.Ordinal)))
                return new SiblingEvidence(LinkSiblingReason.SameDirector, sibling.Title);
            if (candidate.Kind == MediaWorkKind.Series
                && sibling.CatalogKind == MediaWorkKind.Series
                && string.Equals(sibling.CatalogId, candidate.Id, StringComparison.Ordinal))
                return new SiblingEvidence(LinkSiblingReason.SameShow, sibling.Title);
        }

        return null;
    }

    // ── the order ────────────────────────────────────────────────────────────

    private static int YearRank(YearEvidence year) => year.Difference switch
    {
        0 => 0,
        1 => 1,
        _ => 2,
    };

    private static int AgreementRank(bool? agrees) => agrees switch
    {
        true => 0,
        null => 1,
        false => 2,
    };

    private static int GenreRank(LinkGenreAgreement agreement) => agreement switch
    {
        LinkGenreAgreement.Agrees => 0,
        LinkGenreAgreement.Unknown => 1,
        _ => 2,
    };
}
