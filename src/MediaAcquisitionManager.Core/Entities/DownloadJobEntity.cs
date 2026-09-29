using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One operation on a download client: a release sent to a destination, and how it went (written by
/// MAM-13 and MAM-14).
/// </summary>
/// <remarks>
/// The client's and the destination's names are copied onto the row (the audit-row pattern): job
/// history must still read after the integration or the mapping is removed, which sets their foreign
/// keys null.
/// </remarks>
public class DownloadJobEntity : AuditableEntity
{
    /// <summary>The snapshot of the release that was sent; it can't be removed while the job exists.</summary>
    public Guid ReleaseId { get; set; }

    /// <summary>The download client; <c>null</c> once it is removed.</summary>
    public Guid? DownloadClientConnectionId { get; set; }

    /// <summary>The client's name when the job was created.</summary>
    [Required, MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string DownloadClientName { get; set; } = default!;

    /// <summary>The logical destination; <c>null</c> once it is removed.</summary>
    public Guid? DestinationMappingId { get; set; }

    /// <summary>The destination's name when the job was created.</summary>
    [MaxLength(64)]
    public string? DestinationName { get; set; }

    /// <summary>
    /// The client destination key actually sent (the category), so "the category determines the
    /// destination" stays auditable after the mapping changes.
    /// </summary>
    [MaxLength(256)]
    public string? ClientDestinationKey { get; set; }

    /// <summary>The client's stable id of the job once confirmed (an info hash, an <c>nzo_id</c>);
    /// <c>null</c> until then.</summary>
    [MaxLength(128)]
    public string? ClientJobId { get; set; }

    /// <summary>Where the job stands.</summary>
    public DownloadJobStatus Status { get; set; }

    /// <summary>When <see cref="Status"/> last changed.</summary>
    public DateTime StatusChangedUtc { get; set; }

    /// <summary>A readable sentence saying what went wrong, with every secret masked; <c>null</c>
    /// unless the job failed.</summary>
    [MaxLength(1000)]
    public string? Error { get; set; }
}
