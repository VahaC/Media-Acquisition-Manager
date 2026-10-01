using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <inheritdoc cref="IReleaseSearchService"/>
/// <remarks>
/// MAM-12 — the asking is <see cref="IReleaseSources"/>' and the keeping <see cref="IReleaseStore"/>'s,
/// shared with the stored search; this search reads the values, then answers with what was stored.
/// </remarks>
public sealed class ReleaseSearchService(
    ApplicationDbContext db,
    IReleaseSources sources,
    IReleaseStore store,
    IReleaseSearchMapper mapper) : IReleaseSearchService
{
    public async Task<ReleaseSearchResult> SearchAsync(
        string? text,
        string? year,
        string? imdbId,
        string? tmdbId,
        Guid callerUserId,
        CancellationToken cancellationToken = default)
    {
        if (ReleaseSearchTerms.Read(text, year, imdbId, tmdbId, out var terms) is { } error)
            return ReleaseSearchResult.Invalid(error);

        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        var outcomes = await sources.SearchAsync(terms!.Text, terms.Year, terms.Identifiers, cancellationToken);

        var stored = await store.StoreAsync(outcomes, search: null, cancellationToken);
        await store.PruneAsync(cancellationToken);

        return ReleaseSearchResult.Searched(new ReleaseSearchResponse(
            [.. stored.Select(source => mapper.ToDto(source.Outcome, source.ReleaseIds, forOwner))]));
    }
}
