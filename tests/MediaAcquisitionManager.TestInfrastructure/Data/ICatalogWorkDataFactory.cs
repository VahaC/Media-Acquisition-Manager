using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates the metadata catalog's work rows directly in the database (ADR 0025: apart from the
/// library's).</summary>
public interface ICatalogWorkDataFactory
{
    /// <summary>
    /// A saved catalog work of <paramref name="kind"/> and <paramref name="catalogId"/>, with its genres, reported by
    /// <paramref name="sourceConnectionId"/> (named <paramref name="sourceName"/>) at <paramref name="observedUtc"/>
    /// (the clock's now when <c>null</c>). <paramref name="detailsUtc"/> plants a row whose details were read then —
    /// none are planted as child rows — and <paramref name="imdbId"/> what they gave. Its original title is
    /// <paramref name="originalTitle"/>, or <paramref name="title"/> when that is <c>null</c>; its poster path is
    /// <paramref name="posterPath"/>.
    /// </summary>
    Task<CatalogWorkEntity> Create(
        MediaWorkKind kind,
        string catalogId,
        string title,
        DateOnly? releaseDate = null,
        IReadOnlyList<int>? genreIds = null,
        double? rating = 7.5,
        int voteCount = 1000,
        string? imdbId = null,
        DateTime? detailsUtc = null,
        DateTime? observedUtc = null,
        Guid? sourceConnectionId = null,
        string? sourceName = null,
        string? originalTitle = null,
        string? posterPath = null);
}
