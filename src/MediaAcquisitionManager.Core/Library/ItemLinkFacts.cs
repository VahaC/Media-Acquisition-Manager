using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — what <see cref="LinkedIdentifiers"/> needs of a library item's TMDb link (the link's row,
/// <c>LibraryItemLinkEntity</c>).</summary>
/// <param name="Target">Whether the item is the catalog work or an episode of the catalog show.</param>
/// <param name="Scheme">The link's number space: a TMDb film or show.</param>
/// <param name="Value">The link's TMDb id.</param>
/// <param name="SupersededValue">The library's own TMDb id the link replaces; <c>null</c> when it replaced none.</param>
/// <param name="ImdbId">The IMDb id TMDb gave for the film or show; <c>null</c> when it gave none.</param>
/// <param name="WriteState">Where the link stands against the library: for a link to the work itself, whether it is
/// confirmed there decides whether the superseded id still gives way (R6).</param>
public sealed record ItemLinkFacts(
    LibraryLinkTarget Target,
    MediaIdentifierScheme Scheme,
    string Value,
    string? SupersededValue,
    string? ImdbId,
    LibraryLinkWriteState WriteState);
