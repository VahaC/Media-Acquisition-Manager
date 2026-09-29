using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>
/// One release a source lists. A hit has no file list and no work: one release may hold many of
/// both.
/// </summary>
/// <param name="SourceItemId">The source's own id of the listing, with any session removed.</param>
/// <param name="Title">The title as listed; every quality in it is a claim.</param>
/// <param name="SizeBytes">The size the listing claims.</param>
/// <param name="Seeders">The seeders the listing claims.</param>
/// <param name="PublishedUtc">When it was published, in UTC.</param>
/// <param name="SourcePageUrl">The release's page at the source, with any session removed.</param>
/// <param name="CategoryIds">The source's categories it is listed in.</param>
/// <param name="PayloadKind">What it is downloaded from; <c>null</c> when the listing doesn't say.</param>
/// <param name="InfoHash">The info hash when the source states it.</param>
/// <param name="Locator">Where the server fetches the payload from; never sent to the browser.</param>
public sealed record ReleaseSearchHit(
    string SourceItemId,
    string Title,
    long? SizeBytes,
    int? Seeders,
    DateTime? PublishedUtc,
    string? SourcePageUrl,
    IReadOnlyList<string> CategoryIds,
    ReleasePayloadKind? PayloadKind,
    string? InfoHash,
    ReleasePayloadLocator Locator);
