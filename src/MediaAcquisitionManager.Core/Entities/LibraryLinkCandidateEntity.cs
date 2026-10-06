using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — one film or show the catalog answered a library work's search with (<see cref="LibraryLinkSearchEntity"/>),
/// named by kind and id. Its facts — titles, year, runtime, genres, collection, directors — are the
/// <see cref="CatalogWorkEntity"/> row of that kind and id, read when the suggestions are.
/// </summary>
/// <remarks>
/// No key into <see cref="CatalogWorkEntity"/>: ADR 0025 keeps the catalog's tables apart, and a catalog row refreshed
/// or pruned must not take a library work's candidates with it.
/// </remarks>
public class LibraryLinkCandidateEntity : BaseEntity
{
    /// <summary>The search that found it; the candidate is removed with it.</summary>
    public Guid LibraryLinkSearchId { get; set; }

    /// <summary>Where the catalog listed it, from 0, across the work's queries in the order they were asked.</summary>
    public int Position { get; set; }

    /// <summary>A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>).</summary>
    public MediaWorkKind CatalogKind { get; set; }

    /// <summary>The catalog's id of it, in the numbering of its kind.</summary>
    [Required, MaxLength(CatalogWorkEntity.CatalogIdMaximumLength)]
    public string CatalogId { get; set; } = default!;

    /// <summary>Which of the work's queries found it first.</summary>
    public LibraryLinkCandidateSource Source { get; set; }
}
