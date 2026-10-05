using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One inspection of the library's files (MAM-9): queued by a library sync, by a scan of the media
/// folders (MAM-18), by the app's start or by the installation owner, worked through in the background,
/// and polled by the Library page.
/// <see cref="AuditableEntity.CreatedUtc"/> is when it was queued.
/// </summary>
/// <remarks>
/// <para>
/// A run looks at every stored file name — every library's, and the ones a folder scan found that no
/// library reported (MAM-18) — so it is not tied to one integration; at
/// most one is queued and one running at a time. A run queued while another runs exists for the files
/// a sync added meanwhile.
/// </para>
/// <para>
/// History, not state: the requester's name is copied onto the row (the audit-row pattern), and what
/// each file showed is on the file, written as the run goes — a run that stops keeps what it had read.
/// </para>
/// <para>
/// MAM-19, ADR 0022 — its last step hashes the files a byte-identical copy could hide among, one at a
/// time; the hashes are <see cref="FileHashEntity"/> rows, stored as they come, and the files it couldn't
/// hash are counted here with the first one's sentence.
/// </para>
/// </remarks>
public class FileInspectionRunEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> and <see cref="HashProblem"/> the columns hold; a longer
    /// sentence is cut to it.</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>What started it.</summary>
    public FileInspectionTrigger Trigger { get; set; }

    /// <summary>The owner who asked for it; <c>null</c> when a sync, a folder scan or the app's start queued it, or once
    /// that user is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>The requester's user name at the time; <c>null</c> when nobody asked for it.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? RequestedByUserName { get; set; }

    /// <summary>Where the run stands.</summary>
    public FileInspectionRunStatus Status { get; set; }

    /// <summary>When the worker took it; <c>null</c> while it is queued.</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>When it succeeded or failed; <c>null</c> until then.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>How many file names it looks at; <c>null</c> until it has listed them.</summary>
    public int? FilesTotal { get; set; }

    /// <summary>How many of them it has looked at so far: the progress the page shows.</summary>
    public int FilesChecked { get; set; }

    /// <summary>How many files the media inspector read in this run; the others were answered from the
    /// cache or could not be read at all.</summary>
    public int FilesProbed { get; set; }

    /// <summary>
    /// MAM-19, ADR 0022 — how many files its last step found that need a hash: files whose size a file of
    /// another identity shares and whose media facts don't tell the two apart. Counted once per file,
    /// whatever number of names lead to it. <c>null</c> until the step has listed them.
    /// </summary>
    public int? HashCandidates { get; set; }

    /// <summary>How many of them the step has dealt with so far — from the cache, by reading them, or
    /// failing to: the progress the page shows while it hashes.</summary>
    public int HashCandidatesChecked { get; set; }

    /// <summary>How many of them it read in full in this run; the others were answered from the cache or
    /// could not be hashed.</summary>
    public int HashCandidatesRead { get; set; }

    /// <summary>How many of them it could not hash: gone, unreadable, or changed while it read them.
    /// Never a failure of the run.</summary>
    public int HashProblemCount { get; set; }

    /// <summary>Why the first of them it could not hash wasn't hashed, one sentence — quoting its path when
    /// the file inventory gave the reason; <c>null</c> when there was none.</summary>
    [MaxLength(ErrorMaximumLength)]
    public string? HashProblem { get; set; }

    /// <summary>A readable sentence saying why the run failed; <c>null</c> unless it failed.</summary>
    [MaxLength(ErrorMaximumLength)]
    public string? Error { get; set; }
}
