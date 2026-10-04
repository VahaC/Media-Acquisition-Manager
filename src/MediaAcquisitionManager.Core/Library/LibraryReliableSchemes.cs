using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// The registry schemes a work of each kind needs to be matched reliably (specification §3.1): a release
/// may name it by any of them, so a work lacking one is flagged on the Library page and kept by its
/// "missing identifiers" filter (MAM-8).
/// </summary>
/// <remarks>
/// MAM-32 — by kind, as the registries number them (ADR 0017): a movie by IMDb and TMDb; an episode by IMDb
/// and TVDB, as TMDb gives an episode no id of a work; a series by IMDb, TMDb's numbering of shows and TVDB.
/// One list for the mapper and the filter, so a badge and the filter never disagree.
/// </remarks>
public static class LibraryReliableSchemes
{
    private static readonly MediaIdentifierScheme[] Movie = [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb];

    private static readonly MediaIdentifierScheme[] Episode = [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tvdb];

    private static readonly MediaIdentifierScheme[] Series = [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.TmdbTv, MediaIdentifierScheme.Tvdb];

    /// <summary>The schemes a work of <paramref name="kind"/> needs, in the order a list of the missing
    /// ones names them.</summary>
    public static IReadOnlyList<MediaIdentifierScheme> Of(MediaWorkKind kind) => kind switch
    {
        MediaWorkKind.Episode => Episode,
        MediaWorkKind.Series => Series,
        _ => Movie,
    };
}
