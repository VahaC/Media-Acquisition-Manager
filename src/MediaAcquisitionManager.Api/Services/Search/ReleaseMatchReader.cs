using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <inheritdoc cref="IReleaseMatchReader"/>
/// <remarks>
/// MAM-24 — moved out of <see cref="SearchService"/> unchanged, the per-release matching it kept private, when the
/// Download dialog's replacement read came to need the same answer for one release.
/// </remarks>
public sealed class ReleaseMatchReader(ApplicationDbContext db, ILibraryMatcher library) : IReleaseMatchReader
{
    public async Task<ReleaseMatchWorks> ReadWorksAsync(SearchEntity search, bool forOwner, CancellationToken cancellationToken = default)
    {
        var works = (await library.MatchAsync(QueryOf(search), forOwner, cancellationToken)).Works;
        return new ReleaseMatchWorks(works, await EpisodesOfAsync(works, cancellationToken));
    }

    /// <remarks>
    /// <para>
    /// MAM-12 — by <see cref="WorkMatching.Match"/> alone, against every work the search lists: a work the
    /// search lists by its words is matched on the release's own evidence ("Rambo III (1988)" is that
    /// work's title and year), and never by words — a release's name may hold any word at all (ADR 0012).
    /// </para>
    /// <para>
    /// MAM-32 — against the works of its kind alone (ADR 0020): a TV release against series, by a show's
    /// ids (IMDb, TMDb TV, TVDB), then its title with a year its seasons allow
    /// (<see cref="ReleaseWorkClaims.YearsToMatch"/>: its first year exactly when it holds season 1, any
    /// year up to it when only later ones), then its title; a movie release against movies, as before. A film and a show of one title never match each other's releases, and
    /// an episode of no series is matched by none: a release of it would be its show's. A TV match counts
    /// the series' episodes among those the title claims (<see cref="ReleaseMatchWorks.Episodes"/>, by series), each
    /// once however many places it has.
    /// </para>
    /// <para>
    /// MAM-30 — and by what its description claims, read the same way (Vasyl's decision 6, D15): Toloka's description is
    /// its topic's whole title, Ukrainian title first, where a gateway's title keeps the English part alone or, for a
    /// Ukrainian-only release, nothing but punctuation ("« » (2026) WEB-DL-AVC", Jackett's and Prowlarr's alike). The
    /// description adds a match only by an id or by its title and year, as Recommendations count one
    /// (<c>RecommendationAvailabilityPass.IsRelease</c>): a title alone there is no evidence a person should weigh beside
    /// the release's own name, which keeps every level (ADR 0012). A work's match is the strongest of the two, a tie
    /// going to the title's; its held count is counted from the seasons or episodes the claim that matched names, or
    /// the other's when that one names none: both describe the one release, so "Show (S2) WEB-DL" whose description
    /// names the show and its year but no season still holds season 2, as it did when its title alone was read.
    /// </para>
    /// </remarks>
    public SearchRelease Match(ReleaseEntity release, int sourcePosition, string sourceName, ReleaseMatchWorks works)
    {
        var isTv = release.Kind == ReleaseKind.Tv;
        var claimedWork = Claim(isTv, release.Title);
        var descriptionWork = release.Description is { Length: > 0 } description ? Claim(isTv, description) : null;
        var identifiers = ReleaseSearchTerms.IdentifiersOf(release);
        var kind = isTv ? MediaWorkKind.Series : MediaWorkKind.Movie;
        // Every work it matches, never just the best (ADR 0012): strongest first, and among equals in the
        // order the search lists the works — OrderBy is stable.
        List<ReleaseLibraryMatch> matches =
        [
            .. works.Works
                .Where(work => work.Work.Kind == kind)
                .Select(work => Strongest(work, claimedWork, descriptionWork, identifiers) is { } found
                    ? new ReleaseLibraryMatch(
                        work.Work.Id,
                        found.Match,
                        isTv ? HeldCount(works.Episodes[work.Work.Id], found.Episodes) : null,
                        isTv ? found.Episodes : null)
                    : null)
                .OfType<ReleaseLibraryMatch>()
                .OrderBy(match => match.Match.Evidence),
        ];
        return new SearchRelease(
            release, sourcePosition, sourceName, ReleaseTitleClaims.Read(release.Title), claimedWork, identifiers, matches, descriptionWork);
    }

    public async Task<MatchedRelease> MatchAsync(CallerRelease found, bool forOwner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(found);
        var works = await ReadWorksAsync(found.Search, forOwner, cancellationToken);
        return new MatchedRelease(found, Match(found.Release, found.SourcePosition, found.SourceName, works), works);
    }

    /// <summary>The search as the library is matched for it: its text as the one title, its year, its ids.</summary>
    internal static WorkClaim QueryOf(SearchEntity search) =>
        new([search.Text], ClaimedYears.Exactly(search.Year), ReleaseSearchTerms.IdentifiersOf(search.ImdbId, search.TmdbId));

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>What <paramref name="text"/> — a title or a description — claims, read as a TV release's when
    /// <paramref name="isTv"/>.</summary>
    private static ReleaseWorkClaim Claim(bool isTv, string text) => isTv ? ReleaseWorkClaims.ReadTv(text) : ReleaseWorkClaims.Read(text);

    /// <summary>
    /// MAM-30 (D15) — how <paramref name="work"/> matches the release, and the seasons or episodes the claim that
    /// matched names: the title's match at any level, the description's only by an id or by its title and year, the
    /// stronger of the two, the title's on a tie — so the matched title, and what a person was shown before, stays
    /// the title's wherever the description adds nothing. <c>null</c> when neither matches.
    /// </summary>
    /// <remarks>
    /// When the claim that matched names no season, the other's seasons are the release's: a description that names the
    /// show and its year but no season says nothing against the title's "(S2)", and dropping it would lose the held
    /// count and the replacement's episodes the title alone gave before the description was read.
    /// </remarks>
    private static (WorkMatch Match, ClaimedEpisodes? Episodes)? Strongest(
        LibraryWorkMatch work, ReleaseWorkClaim title, ReleaseWorkClaim? description, IReadOnlyList<MediaIdentifier> identifiers)
    {
        var byTitle = MatchOf(title);
        var byDescription = description is null ? null : MatchOf(description) is { Evidence: MatchEvidence.Identifier or MatchEvidence.TitleAndYear } found ? found : null;
        if (byDescription is not null && (byTitle is null || byDescription.Evidence < byTitle.Evidence))
            return (byDescription, description!.Episodes ?? title.Episodes);
        return byTitle is null ? null : (byTitle, title.Episodes ?? description?.Episodes);

        WorkMatch? MatchOf(ReleaseWorkClaim claim) =>
            WorkMatching.Match(new WorkClaim(claim.Titles, ReleaseWorkClaims.YearsToMatch(claim), identifiers), work.Titles, work.Identifiers);
    }

    /// <summary>
    /// MAM-32 — where each episode of the series among <paramref name="libraryWorks"/> is placed, by
    /// series: what a TV release's held count is counted from. One query, whatever the series.
    /// </summary>
    private async Task<ILookup<Guid, (Guid EpisodeId, int? Season, int? Episode)>> EpisodesOfAsync(
        IReadOnlyList<LibraryWorkMatch> libraryWorks, CancellationToken cancellationToken)
    {
        var seriesIds = libraryWorks.Where(work => work.Work.Kind == MediaWorkKind.Series).Select(work => work.Work.Id).ToList();
        var places = await db.MediaEpisodePositions.AsNoTracking()
            .Where(position => position.SeriesWorkId != null && seriesIds.Contains(position.SeriesWorkId.Value))
            .Select(position => new { SeriesWorkId = position.SeriesWorkId!.Value, position.MediaWorkId, position.SeasonNumber, position.EpisodeNumber })
            .ToListAsync(cancellationToken);
        return places.ToLookup(place => place.SeriesWorkId, place => (place.MediaWorkId, place.SeasonNumber, place.EpisodeNumber));
    }

    /// <summary>MAM-32 — how many of a series' episodes, at <paramref name="places"/>, are among those
    /// <paramref name="claimed"/>; <c>null</c> when the release claims none (MAM-30: neither the claim that matched,
    /// the title or the description, nor the other), as nothing says which it holds.</summary>
    private static int? HeldCount(IEnumerable<(Guid EpisodeId, int? Season, int? Episode)> places, ClaimedEpisodes? claimed) =>
        claimed is null
            ? null
            : places.Where(place => claimed.Holds(place.Season, place.Episode)).Select(place => place.EpisodeId).Distinct().Count();
}
