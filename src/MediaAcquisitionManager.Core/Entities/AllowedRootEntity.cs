using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One folder inside the app where it may read files (MAM-9, ADR 0009): the app looks at a library file only
/// when the file, and every link on the way to it, lies inside one of these (specification §4). None
/// by default, so nothing is read until the installation owner names a folder.
/// </summary>
/// <remarks>
/// No <c>CreatedUtc</c>, for the <see cref="PathMappingEntity"/> reason: the whole list is replaced by
/// each Save of the settings page, and the audit row already says when.
/// </remarks>
public class AllowedRootEntity : BaseEntity
{
    /// <summary>The 0-based position as the owner entered it, so the page shows the list back as typed.
    /// Whether a file is inside a root never depends on it.</summary>
    public int Position { get; set; }

    /// <summary>The folder as this app sees it, in <see cref="NormalizedPath"/> canonical form
    /// (<c>/media</c>).</summary>
    [Required, MaxLength(AllowedRootRules.MaximumPathLength)]
    public string Path { get; set; } = default!;
}
