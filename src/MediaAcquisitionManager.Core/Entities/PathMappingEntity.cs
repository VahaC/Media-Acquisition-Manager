using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One prefix pair of an integration's path mapping (ADR 0003): a folder as the service sees it, and
/// the same folder as this app sees it.
/// </summary>
/// <remarks>
/// No <c>CreatedUtc</c>: the whole set is replaced by each Save of its integration, so a time would
/// only say "last saved", which the audit row already says.
/// </remarks>
public class PathMappingEntity : BaseEntity
{
    /// <summary>The integration the pair belongs to; removed with it.</summary>
    public Guid IntegrationConnectionId { get; set; }

    /// <summary>
    /// The 0-based position as the owner entered it, so the dialog shows the pairs back as typed.
    /// Resolving a path never depends on it (<see cref="PathMapper"/>).
    /// </summary>
    public int Position { get; set; }

    /// <summary>The folder as the service sees it, in <see cref="NormalizedPath"/> canonical form
    /// (<c>/downloads</c>).</summary>
    [Required, MaxLength(PathMappingRules.MaximumPathLength)]
    public string ServicePrefix { get; set; } = default!;

    /// <summary>The same folder inside this app's container, in canonical form (<c>/media</c>).</summary>
    [Required, MaxLength(PathMappingRules.MaximumPathLength)]
    public string AppPrefix { get; set; } = default!;
}
