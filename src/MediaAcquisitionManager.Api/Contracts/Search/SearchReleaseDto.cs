using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One release a stored search found, and the library works it may be (MAM-12).</summary>
/// <remarks>
/// Where to download it from never comes here (ADR 0004): the server keeps the link, sealed, and fetches
/// the payload itself when the release is sent to a client (MAM-13). Every value is the listing's claim;
/// the quality, the work and the matches are read from its title and its listing's ids on every read —
/// MAM-30, the matches from its description too, by an id or a title and year.
/// </remarks>
/// <param name="Id">The stored release, for sending it to a download client.</param>
/// <param name="SourceId">The search's source that listed it (<see cref="SearchSourceDto.Id"/>).</param>
/// <param name="SourceName">That source's name when it was searched.</param>
/// <param name="Title">The title as listed.</param>
/// <param name="Description">MAM-33 — the listing's description when it says more than the title: Toloka's
/// own topic title, Ukrainian title first; <c>null</c> when it gives none, or the release was stored before
/// MAM-33.</param>
/// <param name="Kind">MAM-32 — what the source lists it as, by its categories (ADR 0020): <c>Tv</c> is
/// matched to library series only, <c>Movie</c> to movies only.</param>
/// <param name="SizeBytes">The size the listing claims; <c>null</c> when it gives none.</param>
/// <param name="Seeders">The seeders the listing claims; <c>null</c> when it gives none.</param>
/// <param name="Peers">The peers it claims — seeders and leechers together, as the source counts them.</param>
/// <param name="PublishedUtc">When the source published it.</param>
/// <param name="SourcePageUrl">The release's page at the source, with any session removed; <c>null</c>
/// when it gave none the app can show.</param>
/// <param name="PayloadKind"><c>Magnet</c> when the listing links a magnet; <c>null</c> when only the
/// download says.</param>
/// <param name="Claimed">The quality and languages its title claims.</param>
/// <param name="ClaimedWork">The titles and the year its title claims — MAM-32, and a TV release's span and
/// seasons or episodes.</param>
/// <param name="Identifiers">The ids its listing gives, IMDb before TMDb — MAM-32: a show's TMDb id on a TV
/// release, then its TVDB id; claims like its title.</param>
/// <param name="LibraryMatches">The search's library works it matches, strongest evidence first; empty when
/// it matches none of them.</param>
/// <param name="DescriptionWork">MAM-30 — the titles and the year its <paramref name="Description"/> claims, read as
/// <paramref name="ClaimedWork"/> is: a match its title doesn't explain came from here. <c>null</c> when the listing
/// gave no description.</param>
public sealed record SearchReleaseDto(
    Guid Id,
    Guid? SourceId,
    string SourceName,
    string Title,
    string? Description,
    ReleaseKind Kind,
    long? SizeBytes,
    int? Seeders,
    int? Peers,
    DateTime? PublishedUtc,
    string? SourcePageUrl,
    ReleasePayloadKind? PayloadKind,
    ClaimedReleaseQualityDto Claimed,
    ReleaseWorkClaimDto ClaimedWork,
    IReadOnlyList<ReleaseIdentifierDto> Identifiers,
    IReadOnlyList<ReleaseLibraryMatchDto> LibraryMatches,
    ReleaseWorkClaimDto? DescriptionWork);
