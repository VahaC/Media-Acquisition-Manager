using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One library of a media library integration, such as the Jellyfin library "Фільми" (MAM-8): what the
/// Library page filters files by. The inherited <see cref="ProvenancedEntity.SourceItemId"/> is the
/// library's own id.
/// </summary>
/// <remarks>
/// No root folders: a sync decides which library holds each file
/// (<see cref="PhysicalFileEntity.LibraryLocationId"/>) from the folders it has just read, and nothing
/// reads them afterwards yet; MAM-11 may store them for its destination check.
/// </remarks>
public class LibraryLocationEntity : ProvenancedEntity
{
    /// <summary>The longest <see cref="Name"/> the column holds; an adapter cuts a longer one to it.</summary>
    public const int NameMaximumLength = 200;

    /// <summary>The library's name as the service gives it, cleaned of control characters.</summary>
    [Required, MaxLength(NameMaximumLength)]
    public string Name { get; set; } = default!;
}
