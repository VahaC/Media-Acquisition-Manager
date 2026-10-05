using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

// MAM-33 — which releases a source listed are the recommended work (ADR 0012's rules, as the Search page matches a
// release to the library), and what they add up to.
public sealed partial class RecommendationAvailabilityPass
{
    /// <summary>
    /// Whether <paramref name="hit"/> is a release of <paramref name="work"/>: of the work's kind (ADR 0020 — a film's
    /// releases are movies, a show's TV), and matched by an id or by title and year (<see cref="WorkMatching.Match"/>,
    /// never a title alone) from what its title or its description claims. For a held series' missing seasons, only a
    /// release that claims one of them, or names no season at all — a release of seasons the library has adds
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The description too: Toloka's has the Ukrainian title beside the original one ("Інтерстеллар / Interstellar
    /// (2014)"), and Prowlarr's title keeps only one, so a work whose catalog has only a Ukrainian title still
    /// matches.
    /// </remarks>
    private static bool IsRelease(DueWork work, ReleaseSearchHit hit)
    {
        if (hit.Kind != work.ReleaseKind)
            return false;

        List<ReleaseWorkClaim> claims = [Claim(work.Kind, hit.Title)];
        if (hit.Description is { } description)
            claims.Add(Claim(work.Kind, description));
        var matched = claims.Any(claim =>
            WorkMatching.Match(new WorkClaim(claim.Titles, ReleaseWorkClaims.YearsToMatch(claim), hit.Identifiers), work.Titles, work.Identifiers)?.Evidence
                is MatchEvidence.Identifier or MatchEvidence.TitleAndYear);
        if (!matched)
            return false;
        if (work.MissingSeasonNumbers.Count == 0)
            return true;

        var named = claims.Select(claim => claim.Episodes).OfType<ClaimedEpisodes>().ToList();
        return named.Count == 0
            || named.Exists(episodes => work.MissingSeasonNumbers.Any(season => season >= episodes.FirstSeason && season <= episodes.LastSeason));
    }

    /// <summary>What <paramref name="text"/> claims, read as a film's or as a show's release by the work's kind.</summary>
    private static ReleaseWorkClaim Claim(MediaWorkKind kind, string text) =>
        kind == MediaWorkKind.Series ? ReleaseWorkClaims.ReadTv(text) : ReleaseWorkClaims.Read(text);

    /// <summary>
    /// The best of <paramref name="found"/>, by what each title claims (specification §3.1: claimed, never
    /// verified): the highest resolution class, whether any names a Ukrainian audio track, and the most seeders;
    /// <c>null</c> where none says.
    /// </summary>
    private static (int? BestResolution, bool HasUkrainianAudio, int? MaxSeeders) Summarize(IReadOnlyList<ReleaseSearchHit> found)
    {
        var qualities = found.Select(hit => ReleaseTitleClaims.Read(hit.Title)).ToList();
        return (
            qualities.Max(quality => quality.Resolution),
            qualities.Exists(quality => quality.AudioLanguages.Contains("ukr", StringComparer.Ordinal)),
            found.Max(hit => hit.Seeders));
    }
}
