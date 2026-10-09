using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — one library folder the installation owner ticked for replacement on the Media folders page (ADR 0033): an
/// old copy can be picked in the Download dialog only inside a ticked folder. None by default.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by the folder's path inside the app, never by a <see cref="LibraryLocationFolderEntity"/> id: a sync deletes
/// and re-adds those rows, which would drop every tick. Whether a tick still counts is worked out when asked — the path
/// must still be a library folder the mappings bring into the app, inside an allowed root — so a folder that leaves the
/// libraries or the allowed roots loses its tick's effect (ADR 0033) without the row being touched, and gets it back if
/// it returns.
/// </para>
/// <para>
/// No <c>CreatedUtc</c>, for the <see cref="AllowedRootEntity"/> reason: each Save that changes the ticks replaces the
/// whole list, and the audit row says when.
/// </para>
/// </remarks>
public class ReplacementFolderEntity : BaseEntity
{
    /// <summary>The 0-based position in the list the page sent; whether a copy is in a ticked folder never depends on
    /// it.</summary>
    public int Position { get; set; }

    /// <summary>The library folder as this app sees it, in <see cref="NormalizedPath"/> canonical form
    /// (<c>/media/movies</c>).</summary>
    [Required, MaxLength(ReplacementFolderRules.MaximumPathLength)]
    public string Path { get; set; } = default!;
}
