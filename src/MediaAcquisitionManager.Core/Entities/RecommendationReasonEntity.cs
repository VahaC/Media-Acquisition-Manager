using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one reason a <see cref="RecommendationCandidateEntity"/> is recommended, with the facts its chip
/// shows (ADR 0023: every recommendation explains itself). Stored and removed with its candidate.
/// </summary>
public class RecommendationReasonEntity : BaseEntity
{
    /// <summary>The longest <see cref="Titles"/> kept: five library titles and their separators.</summary>
    public const int TitlesMaximumLength = 2000;

    /// <summary>The candidate.</summary>
    public Guid RecommendationCandidateId { get; set; }

    /// <summary>Where the card shows it, from 0: the strongest reason first.</summary>
    public int Position { get; set; }

    /// <summary>What kind of reason it is.</summary>
    public RecommendationReasonKind Kind { get; set; }

    /// <summary>The collection (Collection), the director (Director), the creator (Creator) or the release
    /// source (NewAtSource); <c>null</c> for the other kinds.</summary>
    [MaxLength(CatalogWorkEntity.TitleMaximumLength)]
    public string? Name { get; set; }

    /// <summary>Collection: the released parts the library holds; Director and Creator: their films or shows among
    /// the library's catalog works (the run's seeds); Recommended: how many of the library's catalog works recommend
    /// it. Counted by catalog work, so a film the library holds twice counts once. <c>null</c> for the other
    /// kinds.</summary>
    public int? Held { get; set; }

    /// <summary>Collection: the collection's released parts. <c>null</c> for the other kinds.</summary>
    public int? Total { get; set; }

    /// <summary>Recommended: the display titles of the library works behind the recommending catalog works, each
    /// once, at most five, <c>'\n'</c>-joined; <c>null</c> for the other kinds.</summary>
    [MaxLength(TitlesMaximumLength)]
    public string? Titles { get; set; }

    /// <summary>MissingSeasons: the aired seasons the library lacks, ascending and comma-separated ("4,5");
    /// <c>null</c> for the other kinds.</summary>
    [MaxLength(RecommendationCandidateEntity.MissingSeasonsMaximumLength)]
    public string? Seasons { get; set; }

    /// <summary>NewAtSource: when the release source published it; <c>null</c> for the other kinds, or when the
    /// source gave no date. MAM-33 — an instant, so <c>*Utc</c> (the house rule), as the API's <c>dateUtc</c> is.</summary>
    public DateTime? DateUtc { get; set; }
}
