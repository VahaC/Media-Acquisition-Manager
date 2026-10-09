using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — the installation owner's scheduled replacement of specific old copies by a download (ADR 0030–0034):
/// written with its download job, in the same save, before anything is fetched or sent (ADR 0013), and only from the
/// owner's own ticks in the Download dialog. This version schedules and follows it; verifying the new file and moving
/// the old copies aside are later tickets'.
/// </summary>
/// <remarks>
/// <para>
/// Specification §4's minimum fields, as built: the internal id is <see cref="BaseEntity.Id"/>; the release reference,
/// the payload type, the download client and destination ids, the client's job id and the info hashes are its download
/// job's (<see cref="DownloadJobId"/>: <see cref="DownloadJobEntity.ReleaseId"/>, whose release row
/// <c>ReleaseStore.PruneAsync</c> keeps while a job points at it, <see cref="DownloadJobEntity.PayloadKind"/>,
/// <see cref="DownloadJobEntity.DownloadClientConnectionId"/>, <see cref="DownloadJobEntity.DestinationMappingId"/>,
/// <see cref="DownloadJobEntity.ClientJobId"/>, <see cref="DownloadJobEntity.InfoHashV1"/> and
/// <see cref="DownloadJobEntity.InfoHashV2"/>), never copied twice; the expected work and edition are its
/// <see cref="ReplacementJobWorkEntity"/> rows with their registry ids, and <see cref="ReleaseEditions"/>; the selected
/// old files and their identities are its <see cref="ReplacementJobFileEntity"/> rows; the explicit consent is the row
/// itself with <see cref="RequestedByUserId"/>, <see cref="AuditableEntity.CreatedUtc"/> and its
/// <c>ReplacementScheduled</c> audit row — per work matched by title and year, <see cref="ReplacementJobWorkEntity.ConfirmedByOwner"/>
/// and a <c>ReplacementWorkConfirmed</c> audit row; the verification evidence is MAM-25's to add; the state, its
/// reason and its time are below.
/// </para>
/// <para>
/// Its rows carry no key into the library's rows a sync rebuilds (works, versions, files): they are a snapshot, as
/// <see cref="LibraryItemLinkFileEntity"/> is, so a sync between scheduling and moving can't take its targets away.
/// </para>
/// </remarks>
public class ReplacementJobEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Reason"/> kept, as <see cref="DownloadJobEntity.ErrorMaximumLength"/>.</summary>
    public const int ReasonMaximumLength = DownloadJobEntity.ErrorMaximumLength;

    /// <summary>The longest <see cref="ReleaseEditions"/>: every <see cref="MediaEdition"/> name, comma-separated, fits.</summary>
    public const int ReleaseEditionsMaximumLength = 400;

    /// <summary>The download that brings the new copy; one replacement per download, and the job can't go while this
    /// row points at it.</summary>
    public Guid DownloadJobId { get; set; }

    /// <summary>The owner who scheduled it; <c>null</c> once that account is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>That user's name when it was scheduled, copied (the audit-row pattern).</summary>
    [Required, MaxLength(UserNameRules.MaximumLength)]
    public string RequestedByUserName { get; set; } = default!;

    /// <summary>Where it stands.</summary>
    public ReplacementJobState State { get; set; }

    /// <summary>When <see cref="State"/> last changed.</summary>
    public DateTime StateChangedUtc { get; set; }

    /// <summary>Why it stopped — failed, waiting for review or cancelled —, one readable sentence; <c>null</c> while it
    /// goes on.</summary>
    [MaxLength(ReasonMaximumLength)]
    public string? Reason { get; set; }

    /// <summary>
    /// The edition words the release's title claims, as the owner saw them in the dialog (MAM-37's
    /// <see cref="MediaEdition"/> names, comma-separated, in their order); empty when it claims none. The edition a new
    /// file is expected to be (ADR 0032's "intended edition").
    /// </summary>
    [Required, MaxLength(ReleaseEditionsMaximumLength)]
    public string ReleaseEditions { get; set; } = "";
}
