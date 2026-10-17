using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — which catalog work a library work without the catalog's id, or a title a release source
/// published, most likely is: the cached answer of one lookup, so a run asks the catalog once per refresh
/// age rather than on every run.
/// </summary>
/// <remarks>
/// A possible match only, for recommendations (Vasyl's ticket): it is never shown on the Library page and
/// never written to the library's works (ADR 0025). "Nothing usable" is an answer too, cached the same way,
/// so a title the catalog doesn't know isn't searched for again on every run.
/// </remarks>
public class CatalogLookupEntity : ProvenancedEntity
{
    /// <summary>The longest <see cref="QueryKey"/>, the provenance's own item id's length.</summary>
    public const int QueryKeyMaximumLength = 512;

    /// <summary>
    /// What was looked up, as one key: <c>Movie|imdb|tt0090605</c> by an IMDb id, or
    /// <c>Series|title|YELLOWSTONE|2018</c> by a title's match key and a year.
    /// </summary>
    [Required, MaxLength(QueryKeyMaximumLength)]
    public string QueryKey { get; set; } = default!;

    /// <summary>The kind of the work found; <c>null</c> when nothing usable was.</summary>
    public MediaWorkKind? ResultKind { get; set; }

    /// <summary>The catalog id of the work found; <c>null</c> when nothing usable was.</summary>
    [MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string? ResultCatalogId { get; set; }

    /// <summary>What the work found matched by; <c>null</c> when nothing usable was.</summary>
    public MatchEvidence? Evidence { get; set; }

    /// <summary>
    /// MAM-46 — the catalog language the lookup was asked in (<c>uk-UA</c>, <c>en-US</c>). The catalog matches a title
    /// by the work's title in that language or its original one, so what a lookup by a title found, or that it found
    /// nothing, holds in that language only: the catalog store counts it as fresh only while it is the catalog's language
    /// of now. A lookup by an IMDb id is the same in any language and is never asked again for it. Every lookup cached
    /// before MAM-46 was asked in Ukrainian, which the migration's default says.
    /// </summary>
    [Required, MaxLength(CatalogLanguageRules.MaximumLength)]
    public string Language { get; set; } = default!;
}
