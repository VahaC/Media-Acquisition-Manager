using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ISearchMapper"/>
/// <remarks>
/// A work's ids and a release's claimed quality are mapped by the Library's and the release search's own
/// mappers, so each reads the same on every page.
/// </remarks>
public sealed class SearchMapper(ILibraryMapper libraryMapper, IReleaseSearchMapper releaseMapper) : ISearchMapper
{
    public SearchResponse ToResponse(
        SearchEntity search,
        LibrarySearchResult library,
        IReadOnlyList<SearchSourceEntity> sources,
        IReadOnlyDictionary<Guid, int> releaseCounts,
        IReadOnlyList<string> releaseTitles,
        IReadOnlyList<ReleaseKind> releaseKinds,
        bool forOwner)
    {
        // Read from the titles every time, as each release's own claims are (specification §3.1).
        var claims = releaseTitles.Select(ReleaseTitleClaims.Read).ToList();
        return new SearchResponse(
            search.Id,
            search.Text,
            search.Year,
            search.ImdbId,
            search.TmdbId,
            search.Season,
            search.CreatedUtc,
            [.. library.Works.Select(ToDto)],
            library.UnlistedCount,
            [.. sources.Select(source => new SearchSourceDto(
                source.Id,
                source.IntegrationConnectionId,
                source.Name,
                source.Status,
                source.NotSearchedReason,
                // MAM-10's rule for a source's sentence: it can name the service's address.
                forOwner ? source.Error : null,
                releaseCounts.GetValueOrDefault(source.Id)))],
            new SearchFacetsDto(
                [.. claims.SelectMany(claim => claim.AudioLanguages).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                claims.Any(claim => claim.AudioLanguages.Count == 0),
                [.. claims.Select(claim => claim.Resolution).OfType<int>().Distinct().OrderDescending()],
                claims.Any(claim => claim.Resolution is null),
                [.. releaseKinds.Distinct().Order()]));
    }

    public SearchReleaseDto ToDto(SearchRelease release) =>
        new(
            release.Release.Id,
            release.Release.SearchSourceId,
            release.SourceName,
            release.Release.Title,
            release.Release.Description,
            release.Release.Kind,
            release.Release.SizeBytes,
            release.Release.Seeders,
            release.Release.Peers,
            release.Release.PublishedUtc,
            release.Release.SourcePageUrl,
            release.Release.PayloadKind,
            releaseMapper.ToDto(release.Claimed),
            ToDto(release.ClaimedWork),
            [.. release.Identifiers.Select(identifier => new ReleaseIdentifierDto(identifier.Scheme, identifier.Value))],
            [
                .. release.LibraryMatches.Select(match =>
                    new ReleaseLibraryMatchDto(match.WorkId, match.Match.Evidence, match.Match.ConflictingSchemes, match.HeldEpisodeCount)),
            ],
            release.DescriptionWork is { } descriptionWork ? ToDto(descriptionWork) : null);

    /// <summary>What a title — MAM-30, or a description — claims, as the page shows it.</summary>
    private static ReleaseWorkClaimDto ToDto(ReleaseWorkClaim claim) =>
        new(
            claim.Titles,
            claim.Year,
            claim.LastYear,
            claim.Episodes is { } episodes
                ? new ClaimedEpisodesDto(episodes.FirstSeason, episodes.LastSeason, episodes.FirstEpisode, episodes.LastEpisode)
                : null);

    private LibraryMatchDto ToDto(LibraryWorkMatch match) =>
        new(
            match.Work,
            match.Match.Evidence,
            libraryMapper.ToDtos(match.MatchedIdentifiers),
            match.MatchedTitle is { } title ? new LibraryTitleDto(title.Text, title.Kind, title.Year, title.SourceName) : null,
            match.Match.ConflictingSchemes);
}
