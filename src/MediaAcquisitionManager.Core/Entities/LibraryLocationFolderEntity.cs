using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One root folder of a media library as the server sees it (MAM-11): what a destination's folder is
/// checked against (ADR 0003, consequences) — a destination is expected to save inside one.
/// </summary>
/// <remarks>
/// <para>
/// Not provenanced and no <c>CreatedUtc</c>, for the <see cref="PathMappingEntity"/> reason: a library's
/// folders are part of it, replaced by each sync that reports it, and its own provenance already says who
/// reported it and when.
/// </para>
/// <para>
/// The app's own path is never stored, as for <see cref="PhysicalFileEntity.ServicePath"/>: the destination
/// check maps the folder through its integration's current mappings when it looks.
/// </para>
/// </remarks>
public class LibraryLocationFolderEntity : BaseEntity
{
    /// <summary>The library; a folder says something only about its library and is removed with it.</summary>
    public Guid LibraryLocationId { get; set; }

    /// <summary>The 0-based position in the server's own order of the library's folders.</summary>
    public int Position { get; set; }

    /// <summary>The folder exactly as the server reports it (<c>/media/movies</c>).</summary>
    [Required, MaxLength(PhysicalFileEntity.ServicePathMaximumLength)]
    public string ServicePath { get; set; } = default!;
}
