using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// What a catalog work is about in one language, as the Recommendations page's Description dialog last asked the catalog
/// for it — kept, so the next person to open it reads it from here. A row per language asked, the text <c>null</c> when
/// the catalog has none in it, so that answer is kept too. Read again once older than the catalog's refresh age, and
/// removed with the work.
/// </summary>
/// <remarks>
/// Not <see cref="CatalogWorkEntity.Overview"/>: that one is the details' own, in the catalog's language (Ukrainian), and
/// the TMDb suggestions rank by it; this one is in the language of whoever reads it, English for now, the user's own
/// once the app speaks more than one (ADR 0025's description section).
/// </remarks>
public class CatalogWorkOverviewEntity : BaseEntity
{
    /// <summary>The longest language code kept: <c>en</c>, <c>uk-UA</c>.</summary>
    public const int LanguageMaximumLength = 5;

    /// <summary>The work.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>The language the catalog was asked in, as it was asked: <c>en</c>, <c>uk-UA</c>.</summary>
    [Required, MaxLength(LanguageMaximumLength)]
    public string Language { get; set; } = default!;

    /// <summary>The overview in it; <c>null</c> when the catalog has none in that language.</summary>
    [MaxLength(CatalogWorkEntity.OverviewMaximumLength)]
    public string? Overview { get; set; }

    /// <summary>When the catalog answered.</summary>
    public DateTime FetchedUtc { get; set; }
}
