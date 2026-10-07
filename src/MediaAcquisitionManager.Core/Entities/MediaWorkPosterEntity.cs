using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-35 — a library's own poster of a work: the tag one library item gives its current poster image (ADR 0027). Its
/// <see cref="ProvenancedEntity.SourceItemId"/> is the item whose image it is.
/// </summary>
/// <remarks>
/// A fact with its source, like a title (ADR 0006): one row per library item that has a poster, kept, replaced and removed
/// by the sync exactly as the item's other facts are. A row stores the tag only, never the picture: the picture is fetched
/// from the library when it is first shown and kept on disk under a name the tag makes (<c>LibraryPosterNames</c>), so a
/// new tag is a new picture.
/// </remarks>
public class MediaWorkPosterEntity : ProvenancedEntity
{
    /// <summary>The longest tag a row holds: Jellyfin's are 32 hexadecimal digits.</summary>
    public const int TagMaximumLength = 64;

    /// <summary>The work; the poster is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>The library's own name of the item's current poster image, letters and digits only, which changes
    /// whenever the image does.</summary>
    [Required, MaxLength(TagMaximumLength)]
    public string Tag { get; set; } = default!;
}
