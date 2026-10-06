using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — what the catalog was asked for one library work without a TMDb id, and when: the cache a suggestion run
/// keeps, its candidates (<see cref="LibraryLinkCandidateEntity"/>) under it, and the owner's "none fits".
/// </summary>
/// <remarks>
/// Only the catalog's answer is cached: the evidence, the order and whether a candidate is confident are computed when
/// the suggestions are read (decision 7), so a runtime measured later, or a neighbour linked later, changes them without
/// asking the catalog again.
/// </remarks>
public class LibraryLinkSearchEntity : AuditableEntity
{
    /// <summary>The longest <see cref="QueryKey"/> kept.</summary>
    public const int QueryKeyMaximumLength = 2000;

    /// <summary>The library work searched for; the search is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>
    /// The kinds, queries and year searched, joined: a work whose titles, folder or year change gets a key of its own and
    /// is searched again.
    /// </summary>
    [Required, MaxLength(QueryKeyMaximumLength)]
    public string QueryKey { get; set; } = default!;

    /// <summary>When the catalog was last asked.</summary>
    public DateTime SearchedUtc { get; set; }

    /// <summary>When the owner said none of the suggestions fits (the review's "0"); <c>null</c> unless they did.</summary>
    public DateTime? NoneFitsUtc { get; set; }

    /// <summary>The user who said so; <c>null</c> when nobody did, or once that user is deleted.</summary>
    public Guid? NoneFitsByUserId { get; set; }
}
