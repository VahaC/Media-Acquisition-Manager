using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One scan of the media folders for the files no library reported (MAM-18, ADR 0021): queued by the
/// installation owner, worked through in the background, and polled by the Library page.
/// <see cref="AuditableEntity.CreatedUtc"/> is when it was queued.
/// </summary>
/// <remarks>
/// <para>
/// A scan walks every allowed folder, so it is not tied to an integration; at most one is queued and one
/// running at a time. It stores what it found in one go when the walk is over, so a scan that stops
/// stores nothing.
/// </para>
/// <para>
/// History, not state: the requester's name is copied onto the row (the audit-row pattern), and the names
/// it found are <see cref="PhysicalFileEntity"/> rows of their own.
/// </para>
/// </remarks>
public class FolderScanRunEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> and <see cref="Problem"/> the columns hold; a longer
    /// message is fitted into it (ADR 0042).</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>The owner who asked for it; <c>null</c> once that user is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>The requester's user name at the time.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? RequestedByUserName { get; set; }

    /// <summary>Where the scan stands.</summary>
    public FolderScanStatus Status { get; set; }

    /// <summary>When the worker took it; <c>null</c> while it is queued.</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>When it succeeded or failed; <c>null</c> until then.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>How many media files the walk has reached so far — each file once, however many links lead
    /// to it: the progress the page shows, and the total once the scan succeeded.</summary>
    public int FilesFound { get; set; }

    /// <summary>How many of them no library reported; <c>null</c> until the scan stored what it found.</summary>
    public int? FilesUnreported { get; set; }

    /// <summary>How many folders, links and names the walk didn't go into or through.</summary>
    public int ProblemCount { get; set; }

    /// <summary>The first of those problems, in walk order, as a message the Api renders (ADR 0042); <c>null</c> when
    /// there was none.</summary>
    [MaxLength(ErrorMaximumLength)]
    public Message? Problem { get; set; }

    /// <summary>Why the scan failed, as a message the Api renders (ADR 0042); <c>null</c> unless it failed.</summary>
    [MaxLength(ErrorMaximumLength)]
    public Message? Error { get; set; }
}
