using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A registry id a source claims for a work (ADR 0006).</summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id as the source gave it (<c>tt0090605</c>, <c>679</c>).</param>
/// <param name="SourceName">The name of the integration that claimed it when it did; <c>null</c> when no
/// integration did. MAM-34 — for an id the owner linked by hand, the name of the user who set it, for the installation
/// owner only: <c>null</c> for everyone else.</param>
/// <param name="SourceKind">MAM-34 — what kind of source claimed it: the library (<c>MediaLibrary</c>), a person's TMDb
/// link (<c>Person</c>), or the catalog for the IMDb id TMDb gave for a linked work (<c>Metadata</c>). Appended with a
/// default, like the one after it, so every construction of before still compiles.</param>
/// <param name="SetUtc">MAM-34 — when a person set it; <c>null</c> for an id a library or the catalog reported.</param>
public sealed record LibraryIdentifierDto(
    MediaIdentifierScheme Scheme,
    string Value,
    string? SourceName,
    ProvenanceSourceKind SourceKind = ProvenanceSourceKind.MediaLibrary,
    DateTime? SetUtc = null);
