using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Media works for a test, with what a library reported about them.</summary>
public interface IMediaWorkSeeder
{
    /// <summary>
    /// A movie with a title, an identifier, a version and a file — each asserted by
    /// <paramref name="source"/>, with its id and name as the provenance of the row.
    /// </summary>
    Task<MediaWorkEntity> Create(IntegrationConnectionEntity source);

    /// <summary>
    /// MAM-33 — a movie as <paramref name="source"/> reported it: its displayed <paramref name="title"/> of
    /// <paramref name="year"/>, an original title when one is given, and the TMDb and IMDb ids given; no version or file
    /// (what the recommendations read of a library is its titles and ids) unless <paramref name="fileSizes"/> says
    /// otherwise: then a version with one file of each size, as the library reported them.
    /// </summary>
    Task<MediaWorkEntity> CreateMovie(
        IntegrationConnectionEntity source,
        string title,
        int? year,
        string? tmdbId = null,
        string? imdbId = null,
        string? originalTitle = null,
        IReadOnlyList<long>? fileSizes = null);

    /// <summary>
    /// MAM-33 — a series as <paramref name="source"/> reported it: its displayed <paramref name="title"/> of
    /// <paramref name="year"/>, the TMDb TV and IMDb ids given, and one episode in each of <paramref name="seasons"/>
    /// (season 1 when <c>null</c>; a series holding no episode isn't in the library, MAM-32), each with a version and a
    /// file of <paramref name="episodeSizeBytes"/> when that is given.
    /// </summary>
    Task<MediaWorkEntity> CreateSeries(
        IntegrationConnectionEntity source,
        string title,
        int? year,
        string? tmdbTvId = null,
        string? imdbId = null,
        IReadOnlyList<int>? seasons = null,
        long? episodeSizeBytes = null);

    /// <summary>
    /// MAM-8 — one more episode of <paramref name="series"/>, at season <paramref name="season"/>, number 1, created at
    /// the factory's now; with a version and a file of <paramref name="sizeBytes"/> when that is given.
    /// </summary>
    Task<MediaWorkEntity> AddEpisode(
        IntegrationConnectionEntity source,
        MediaWorkEntity series,
        int season,
        long? sizeBytes = null);
}
