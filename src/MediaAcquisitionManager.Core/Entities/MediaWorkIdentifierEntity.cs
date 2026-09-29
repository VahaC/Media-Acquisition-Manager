using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// A registry id one source claims for a work (specification §3.1's reliable identifiers).
/// </summary>
/// <remarks>
/// Two sources claiming different values for one work are two rows: the conflict that blocks
/// automatic matching (§3.1) must be storable, not overwritten by whichever source synced last.
/// </remarks>
public class MediaWorkIdentifierEntity : ProvenancedEntity
{
    /// <summary>The work; the identifier is removed with it.</summary>
    public Guid MediaWorkId { get; set; }

    /// <summary>The registry the value belongs to.</summary>
    public MediaIdentifierScheme Scheme { get; set; }

    /// <summary>The id as the source gave it (<c>tt0090605</c>, <c>679</c>).</summary>
    [Required, MaxLength(64)]
    public string Value { get; set; } = default!;
}
