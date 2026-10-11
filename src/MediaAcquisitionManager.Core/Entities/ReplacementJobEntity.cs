using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — the installation owner's scheduled replacement of specific old copies by a download (ADR 0030–0034):
/// written with its download job, in the same save, before anything is fetched or sent (ADR 0013), and only from the
/// owner's own ticks in the Download dialog. MAM-24 schedules and follows it, MAM-25 verifies its new files (ADR 0035),
/// and MAM-26 moves the old copies into the quarantine and keeps them there until the owner restores them or deletes
/// them for good (ADR 0030), name by name in its <see cref="ReplacementJobNameEntity"/> rows.
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
/// and a <c>ReplacementWorkConfirmed</c> audit row; the verification evidence is MAM-25's
/// <see cref="ReplacementJobNewFileEntity"/> rows with their ids, the pair and problem columns of its
/// <see cref="ReplacementJobFileEntity"/> rows, <see cref="InspectionStartedUtc"/> and <see cref="LibraryAskedUtc"/>; the
/// quarantine — where each name went, its identity before the move, and what became of it — is MAM-26's
/// <see cref="ReplacementJobNameEntity"/> rows with <see cref="QuarantineCheckedUtc"/>, <see cref="QuarantinedUtc"/> and
/// <see cref="LibraryNoticeDueUtc"/>; the state, its reason and its time are below.
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

    /// <summary>
    /// MAM-25 — when the job entered <see cref="ReplacementJobState.Inspecting"/>; <c>null</c> before. The client's word
    /// and the files' look must be settled within <c>ReplacementVerificationRules.FilesWait</c> of it. Its own column
    /// rather than <see cref="StateChangedUtc"/>, which a later move of the state would take away.
    /// </summary>
    public DateTime? InspectionStartedUtc { get; set; }

    /// <summary>
    /// MAM-25 — when the app asked the library to look at the new files' paths, the request having been taken;
    /// <c>null</c> before, which is also what tells the verifier the files' checks are still to pass. The library must
    /// list, refresh and identify every new file within <c>ReplacementVerificationRules.LibraryWait</c> of it.
    /// </summary>
    public DateTime? LibraryAskedUtc { get; set; }

    /// <summary>
    /// MAM-26 — when the mover last stopped this job before moving anything (its checks 5 and 6) or claimed it to move;
    /// <c>null</c> before. It marks a job the mover's stage stopped: the owner's Retry is offered for a
    /// <see cref="ReplacementJobState.NeedsReview"/> job only while it is set, as a stop of MAM-25's check isn't the
    /// mover's to retry. MAM-25's verifier never sets it. Nullable and with no key, so the upgrade rebuilds no table.
    /// </summary>
    public DateTime? QuarantineCheckedUtc { get; set; }

    /// <summary>MAM-26 — when the job reached <see cref="ReplacementJobState.Quarantined"/>; <c>null</c> before, and for a
    /// job that never did.</summary>
    public DateTime? QuarantinedUtc { get; set; }

    /// <summary>
    /// MAM-26 — since when the library is owed a notice that the job's names left or came back (ADR 0030: Jellyfin hears
    /// <c>POST /Library/Media/Updated</c> after every move and every restore); <c>null</c> while none is owed. Cleared
    /// once the library took it, or was given up on.
    /// </summary>
    public DateTime? LibraryNoticeDueUtc { get; set; }
}
