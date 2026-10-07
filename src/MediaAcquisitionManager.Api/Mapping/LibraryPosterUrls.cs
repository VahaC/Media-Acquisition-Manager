using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>
/// MAM-35 — where the app serves a library work's poster: <c>LibraryController</c>'s posters route, which the SPA fetches
/// with the user's token and refuses to send anywhere else (ADR 0027). Never the catalog's route
/// (<see cref="CatalogPosterUrls"/>): a Library card shows the library's own picture.
/// </summary>
public static class LibraryPosterUrls
{
    /// <summary>The route's prefix; the file name follows it.</summary>
    public const string Prefix = "/api/library/posters/";

    /// <summary>The URL of the poster <paramref name="posterTag"/> of the library item <paramref name="sourceItemId"/>;
    /// <c>null</c> for none, or one the route wouldn't serve — so a card never asks for one that can only 404.</summary>
    public static string? Of(string? sourceItemId, string? posterTag) =>
        LibraryPosterNames.FileNameOf(sourceItemId, posterTag) is { } fileName ? Prefix + fileName : null;
}
