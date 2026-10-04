using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One search of the search page, as it was asked (MAM-12): the library and every enabled release source
/// are asked once, and every filter, sort and page reads what this search stored (ADR 0011) — changing a
/// filter never asks the sources again, and a reload keeps the results.
/// </summary>
/// <remarks>
/// <para>
/// A search is its user's: every signed-in user may search (MAM-10's choice), and another user's search
/// reads as missing. It lives as long as its releases — a day, the lifetime of a release no download job
/// uses (MAM-10) — and is removed by the prune of a later search; its sources go with it.
/// </para>
/// <para>
/// The values as asked, trimmed; the library's side is matched again on every read of the search, so a
/// sync since shows.
/// </para>
/// </remarks>
public class SearchEntity : AuditableEntity
{
    /// <summary>The user who searched; the search is removed with them.</summary>
    public Guid UserId { get; set; }

    /// <summary>The title searched for, required like every search's (<see cref="ReleaseSearchRules"/>).</summary>
    [Required, MaxLength(ReleaseSearchRules.TextMaximumLength)]
    public string Text { get; set; } = default!;

    /// <summary>The year asked for; <c>null</c> when none was.</summary>
    public int? Year { get; set; }

    /// <summary>The IMDb id asked for (<c>tt0816692</c>); <c>null</c> when none was. 64, like a work
    /// identifier's value.</summary>
    [MaxLength(64)]
    public string? ImdbId { get; set; }

    /// <summary>The TMDb id asked for (<c>157336</c>), a movie's; <c>null</c> when none was.</summary>
    [MaxLength(64)]
    public string? TmdbId { get; set; }

    /// <summary>The season asked for (MAM-32): then the sources are asked for that season of a show only,
    /// and for no movie. <c>null</c> when none was.</summary>
    public int? Season { get; set; }
}
