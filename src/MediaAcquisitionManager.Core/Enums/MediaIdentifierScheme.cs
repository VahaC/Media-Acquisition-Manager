namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// A public registry whose id names one work — the reliable identifiers specification §3.1 matches
/// on first. Registries, not integration products: the app never talks to them in Phase A. Stored by
/// name.
/// </summary>
/// <remarks>
/// <para>
/// A library's collection id (Jellyfin's <c>TmdbCollection</c>) is deliberately not a scheme here: it
/// names a box set, not a work, and stored as a work identifier it would make every movie of a
/// collection match every other (VendorSamples README).
/// </para>
/// <para>
/// MAM-32 — TMDb numbers movies and shows separately, so the same number can name both (ADR 0017): a
/// show's TMDb id is <see cref="TmdbTv"/>, never <see cref="Tmdb"/>, and the two can't match. TVDB ids
/// name a show on a series and an episode on an episode; works are grouped and matched by kind, so the
/// two never meet.
/// </para>
/// </remarks>
public enum MediaIdentifierScheme
{
    /// <summary>An IMDb title id, such as <c>tt0090605</c>: a movie's, a show's or an episode's.</summary>
    Imdb = 1,

    /// <summary>A TMDb movie id, such as <c>679</c>.</summary>
    Tmdb = 2,

    /// <summary>A TVDB id, such as <c>341164</c>: a show's on a series, an episode's on an episode
    /// (MAM-32).</summary>
    Tvdb = 3,

    /// <summary>A TMDb TV id, such as <c>73586</c>: a show's, in TMDb's numbering of shows (MAM-32).</summary>
    TmdbTv = 4,
}
