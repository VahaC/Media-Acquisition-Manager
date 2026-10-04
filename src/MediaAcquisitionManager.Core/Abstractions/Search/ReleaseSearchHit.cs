using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>
/// One release a source lists. A hit has no file list and no work: one release may hold many of
/// both.
/// </summary>
/// <remarks>
/// MAM-10 — every field but <see cref="Locator"/> is safe to store in clear and to show: the adapter
/// removes any session from a link, refuses a link that carries a credential, and replaces an id that
/// does with a digest of it. The record's generated <c>ToString</c> prints them, and the locator
/// masked.
/// </remarks>
/// <param name="SourceItemId">The source's own id of the listing, with any session removed, or a digest of
/// it when it carries a credential; <c>null</c> when the listing gives none.</param>
/// <param name="Title">The title as listed; every quality in it is a claim.</param>
/// <param name="SizeBytes">The size the listing claims.</param>
/// <param name="Seeders">The seeders the listing claims.</param>
/// <param name="Peers">The peers the listing claims: seeders and leechers together, as the source counts
/// them (MAM-10).</param>
/// <param name="PublishedUtc">When it was published, in UTC.</param>
/// <param name="SourcePageUrl">The release's page at the source, with any session removed.</param>
/// <param name="CategoryIds">The source's categories it is listed in.</param>
/// <param name="PayloadKind">What it is downloaded from; <c>null</c> when the listing doesn't say.</param>
/// <param name="InfoHash">The info hash when the source states it.</param>
/// <param name="Identifiers">The registry ids the listing gives (MAM-12): claims of the listing, as its
/// title's are, at most one per scheme, IMDb first; none of a scheme the listing gives two values of, and
/// empty when it gives none. A TV listing's TMDb id is a show's (<see cref="MediaIdentifierScheme.TmdbTv"/>,
/// MAM-32).</param>
/// <param name="Locator">Where the server fetches the payload from; never sent to the browser.</param>
/// <param name="Kind">Whether the source lists it as a movie or as TV, from its categories (MAM-32): the
/// adapter knows its protocol's numbering.</param>
public sealed record ReleaseSearchHit(
    string? SourceItemId,
    string Title,
    long? SizeBytes,
    int? Seeders,
    int? Peers,
    DateTime? PublishedUtc,
    string? SourcePageUrl,
    IReadOnlyList<string> CategoryIds,
    ReleasePayloadKind? PayloadKind,
    string? InfoHash,
    IReadOnlyList<MediaIdentifier> Identifiers,
    ReleasePayloadLocator Locator,
    ReleaseKind Kind);
