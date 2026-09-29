using System.ComponentModel.DataAnnotations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// A logical destination ("Movies", "Movies 4K") and the download client's own name for where such
/// downloads go (specification §3.2). The table exists since MAM-7; MAM-11 builds its settings.
/// </summary>
/// <remarks>
/// No save-path column on purpose: specification §3.2 forbids overriding a client's category with a
/// save path, so the only thing the app ever sends is the client's own destination key.
/// </remarks>
public class DestinationMappingEntity : AuditableEntity
{
    /// <summary>The download client whose configuration the destination names; removed with it.</summary>
    public Guid DownloadClientConnectionId { get; set; }

    /// <summary>The logical destination's name, trimmed.</summary>
    [Required, MaxLength(64)]
    public string Name { get; set; } = default!;

    /// <summary>The name trimmed and upper-cased; unique per client in any letter case.</summary>
    [Required, MaxLength(64)]
    public string NormalizedName { get; set; } = default!;

    /// <summary>The client's own name for the destination, such as a qBittorrent category.</summary>
    [Required, MaxLength(256)]
    public string ClientDestinationKey { get; set; } = default!;
}
