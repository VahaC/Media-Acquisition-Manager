namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// A public registry whose id names one work — the reliable identifiers specification §3.1 matches
/// on first. Registries, not integration products: the app never talks to them in Phase A. Stored by
/// name.
/// </summary>
/// <remarks>
/// A library's collection id (Jellyfin's <c>TmdbCollection</c>) is deliberately not a scheme here: it
/// names a box set, not a work, and stored as a work identifier it would make every movie of a
/// collection match every other (VendorSamples README).
/// </remarks>
public enum MediaIdentifierScheme
{
    /// <summary>An IMDb title id, such as <c>tt0090605</c>.</summary>
    Imdb = 1,

    /// <summary>A TMDb movie id, such as <c>679</c>.</summary>
    Tmdb = 2,
}
