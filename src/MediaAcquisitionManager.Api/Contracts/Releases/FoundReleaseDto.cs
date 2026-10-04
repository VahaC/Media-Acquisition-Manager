using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One release a source listed, as the search stored it (MAM-10).</summary>
/// <remarks>
/// Where to download it from never comes here (ADR 0004): the server keeps the link, sealed, and fetches
/// the payload itself when the release is sent to a client (MAM-13). Every value is the listing's claim.
/// </remarks>
/// <param name="Id">The stored release, for sending it to a download client. A new id every search: the
/// same listing found twice is two snapshots.</param>
/// <param name="Title">The title as listed.</param>
/// <param name="Kind">MAM-32 — what the source lists it as, by its categories (ADR 0020): <c>Tv</c> for a
/// series, its seasons or episodes, cartoon series included; <c>Movie</c> otherwise.</param>
/// <param name="SizeBytes">The size the listing claims; <c>null</c> when it gives none.</param>
/// <param name="Seeders">The seeders the listing claims; <c>null</c> when it gives none.</param>
/// <param name="Peers">The peers it claims — seeders and leechers together, as the source counts them.</param>
/// <param name="PublishedUtc">When the source published it.</param>
/// <param name="SourcePageUrl">The release's page at the source, with any session removed; <c>null</c>
/// when it gave none the app can show.</param>
/// <param name="Categories">The source's categories it is listed in.</param>
/// <param name="PayloadKind"><c>Magnet</c> when the listing links a magnet; <c>null</c> when only the
/// download says (a gateway's link can turn out to be a magnet).</param>
/// <param name="Claimed">The quality and languages its title claims.</param>
public sealed record FoundReleaseDto(
    Guid Id,
    string Title,
    ReleaseKind Kind,
    long? SizeBytes,
    int? Seeders,
    int? Peers,
    DateTime? PublishedUtc,
    string? SourcePageUrl,
    IReadOnlyList<ReleaseCategoryDto> Categories,
    ReleasePayloadKind? PayloadKind,
    ClaimedReleaseQualityDto Claimed);
