using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One operation on a download client: a release sent to a destination, and how it went (written by
/// MAM-13 and MAM-14).
/// </summary>
/// <remarks>
/// <para>
/// The client's and the destination's names are copied onto the row (the audit-row pattern): job
/// history must still read after the integration or the mapping is removed, which sets their foreign
/// keys null.
/// </para>
/// <para>
/// MAM-13 — the row is written before anything is fetched or sent, with the id the browser gave the
/// request (<see cref="RequestId"/>), so a repeated click or a retry of that request finds this row instead
/// of sending the release again (specification §3.2, step 5; ADR 0013).
/// </para>
/// </remarks>
public class DownloadJobEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> kept; a longer sentence is cut with "…".</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>
    /// The id the browser gave the request that created the job (MAM-13): an idempotency key, unique per
    /// requesting user, so the same request sent again answers this job and sends nothing.
    /// </summary>
    public Guid RequestId { get; set; }

    /// <summary>The user who sent the release; <c>null</c> once that user is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>That user's name when the job was created.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? RequestedByUserName { get; set; }

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
    [MaxLength(ErrorMaximumLength)]
    public string? Error { get; set; }

    /// <summary>Whose sentence <see cref="Error"/> is (MAM-13): a service's goes to the owner only, as it can
    /// name the service's address; <c>null</c> without an error.</summary>
    public DownloadJobErrorOrigin? ErrorOrigin { get; set; }

    /// <summary>
    /// The torrent's v1 info hash as the app worked it out from the payload it sent (MAM-13), lower-case;
    /// <c>null</c> before the payload is fetched, for a torrent without v1 metadata, or when the payload names
    /// no torrent the app can read.
    /// </summary>
    /// <remarks>
    /// Both hashes are kept, not one: the client's job is found by them when the client gave no id, and a
    /// client names a hybrid torrent by either — qBittorrent built on libtorrent 2 by the v2 hash cut to 40
    /// characters, one built on libtorrent 1.2 by the v1 hash.
    /// </remarks>
    [MaxLength(40)]
    public string? InfoHashV1 { get; set; }

    /// <summary>The torrent's v2 info hash, the same way (MAM-13); <c>null</c> for a torrent without v2
    /// metadata.</summary>
    /// <remarks>MAM-14 — or as the client lists it, for a job sent without one (a magnet naming only its v1
    /// hash) once the client knows it: what finds the job again after the client renames it to it.</remarks>
    [MaxLength(64)]
    public string? InfoHashV2 { get; set; }

    /// <summary>
    /// MAM-14 — how far the client has downloaded it, from 0 to 1, as the client last reported it; <c>null</c>
    /// until the client has. A report that says nothing about the download (none at all, a re-check's) keeps
    /// the one stored.
    /// </summary>
    public double? Progress { get; set; }

    /// <summary>
    /// MAM-14 — why a followed job isn't moving on, every secret masked: the client reports an error for it,
    /// no longer lists it, or can't be asked (ADR 0014). <c>null</c> when nothing blocks it. Not an end: the job
    /// keeps its status and is still followed, and the reason goes once what caused it does (MAM-14's
    /// decisions 2 and 3).
    /// </summary>
    [MaxLength(ErrorMaximumLength)]
    public string? BlockReason { get; set; }

    /// <summary>MAM-14 — whose sentence <see cref="BlockReason"/> is, with <see cref="ErrorOrigin"/>'s rule: a
    /// client's own goes to the owner only, as it can name the client's address; <c>null</c> without a
    /// reason.</summary>
    public DownloadJobErrorOrigin? BlockReasonOrigin { get; set; }

    /// <summary>
    /// MAM-38, ADR 0034 — how the release was checked, before it was sent, for files it would write over: checked,
    /// or sent unchecked and why. <c>null</c> for a job sent before the check existed, or one the check didn't let
    /// through — a refused one fails with its reason. A job whose submission failed after the check keeps it.
    /// </summary>
    public DownloadOverwriteCheck? OverwriteCheck { get; set; }
}
