using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — a film or a show as the metadata catalog describes it (ADR 0025: the catalog is kept apart from
/// the library). Never a library work: nothing here is written to <see cref="MediaWorkEntity"/> or its
/// titles and identifiers, and nothing of the library is written here.
/// </summary>
/// <remarks>
/// <para>
/// One row per work of the catalog, keyed by <see cref="Kind"/> and <see cref="CatalogId"/>: the catalog
/// numbers films and shows apart (ADR 0017), so the same id can name both. Its provenance is the catalog
/// integration that last reported it (<see cref="ProvenancedEntity.SourceItemId"/> <c>movie/603</c>,
/// <c>tv/73586</c>), and <see cref="ProvenancedEntity.ObservedUtc"/> when it did — in any list or in its
/// details.
/// </para>
/// <para>
/// The summary columns are refreshed whenever a list or the details report the work; the details-only ones
/// — <see cref="ImdbId"/>, the collection, MAM-34's runtime and overview, and the child rows of people, seasons,
/// recommendations and alternative titles — only
/// by its details, which <see cref="DetailsUtc"/> dates. A work known from lists alone has
/// <see cref="DetailsUtc"/> <c>null</c>.
/// </para>
/// </remarks>
public class CatalogWorkEntity : ProvenancedEntity
{
    /// <summary>The longest catalog id kept: TMDb's are a few digits, and room is left for another numbering.</summary>
    public const int CatalogIdMaximumLength = 32;

    /// <summary>The longest title kept, as a library title's (<see cref="MediaWorkTitleEntity.TextMaximumLength"/>).</summary>
    public const int TitleMaximumLength = MediaWorkTitleEntity.TextMaximumLength;

    /// <summary>A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</summary>
    public MediaWorkKind Kind { get; set; }

    /// <summary>The catalog's id of the work, in the numbering of its kind ("603").</summary>
    [Required, MaxLength(CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;

    /// <summary>The title in the catalog's language (Ukrainian), or the catalog's fallback.</summary>
    [Required, MaxLength(TitleMaximumLength)]
    public string Title { get; set; } = default!;

    /// <summary>The title in the work's original language; <c>null</c> when the catalog gives none.</summary>
    [MaxLength(TitleMaximumLength)]
    public string? OriginalTitle { get; set; }

    /// <summary>The original language's ISO 639-1 code; <c>null</c> when the catalog gives none.</summary>
    [MaxLength(16)]
    public string? OriginalLanguage { get; set; }

    /// <summary>A film's first release or a show's first air date; <c>null</c> when not known, as for a
    /// work still to come.</summary>
    public DateOnly? ReleaseDate { get; set; }

    /// <summary>The average rating, 0 to 10; <c>null</c> when nobody rated it.</summary>
    public double? Rating { get; set; }

    /// <summary>How many ratings <see cref="Rating"/> is the average of.</summary>
    public int VoteCount { get; set; }

    /// <summary>The catalog's path of its poster (<c>/abc.jpg</c>); <c>null</c> when it has none.</summary>
    [MaxLength(200)]
    public string? PosterPath { get; set; }

    /// <summary>The longest <see cref="Overview"/> kept; a longer one is cut to it.</summary>
    public const int OverviewMaximumLength = 2000;

    /// <summary>A film's or — MAM-34 — a show's IMDb id, from its details; <c>null</c> for a work known from lists
    /// only, or one the catalog gives none.</summary>
    [MaxLength(64)]
    public string? ImdbId { get; set; }

    /// <summary>
    /// MAM-34 — a film's runtime, or a show's episodes', in minutes, from its details: what a library work's measured
    /// duration is compared with; <c>null</c> when not known.
    /// </summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>MAM-34 — what the work is about, from its details, for the owner choosing a TMDb link; <c>null</c>
    /// when the catalog gives none.</summary>
    [MaxLength(OverviewMaximumLength)]
    public string? Overview { get; set; }

    /// <summary>The catalog's id of the collection a film belongs to, from its details; <c>null</c> otherwise.</summary>
    [MaxLength(CatalogIdMaximumLength)]
    public string? CollectionId { get; set; }

    /// <summary>That collection's name in the catalog's language.</summary>
    [MaxLength(TitleMaximumLength)]
    public string? CollectionName { get; set; }

    /// <summary>When its details — people, seasons, recommendations — were last read; <c>null</c> for a work
    /// known from lists only.</summary>
    public DateTime? DetailsUtc { get; set; }

    /// <summary>
    /// MAM-34 — which reading of the details the row holds: 0 for the ones read before runtime, overview, alternative
    /// titles and a show's IMDb id were kept. The catalog store counts details as fresh only from its current version
    /// on (<c>CatalogStore.CurrentDetailsVersion</c>), so every row cached before is read again on its next use — with
    /// no hand-written migration statement to clear them (R22).
    /// </summary>
    public int DetailsVersion { get; set; }
}
