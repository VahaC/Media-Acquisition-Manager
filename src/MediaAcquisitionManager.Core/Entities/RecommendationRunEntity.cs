using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — one computation of the recommendations: queued by a library sync, a newly connected catalog, a
/// tab switched on, the newest releases read, or the installation owner, worked through in the background,
/// and read by the Recommendations page. <see cref="AuditableEntity.CreatedUtc"/> is when it was queued.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="FileInspectionRunEntity"/> shape: at most one queued and one running at a time; a trigger
/// that finds one queued joins it. History, not state: the requester's name is copied onto the row (the
/// audit-row pattern).
/// </para>
/// <para>
/// A run computes the candidates of every user at once — what the library lacks is the installation's — and
/// replaces them in one transaction when it succeeds; a run that fails leaves the previous ones as they
/// were. Who sees which is decided when the list is read (each user's tabs and "Not interested").
/// </para>
/// </remarks>
public class RecommendationRunEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> the column holds; a longer message is fitted into it (ADR 0042).</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>What queued it.</summary>
    public RecommendationRunTrigger Trigger { get; set; }

    /// <summary>The user who asked for it; <c>null</c> when the app queued it, or once that user is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>The requester's user name at the time; <c>null</c> when nobody asked for it.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? RequestedByUserName { get; set; }

    /// <summary>Where the run stands.</summary>
    public RecommendationRunStatus Status { get; set; }

    /// <summary>When the worker took it; <c>null</c> while it is queued.</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>When it succeeded or failed; <c>null</c> until then.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>
    /// What the running run is doing, for the page to say more than "Computing…" (Vasyl asked); <c>null</c> before
    /// its first stage and once it has ended — every write that ends a run clears it, so a finished run never shows a
    /// stage it left.
    /// </summary>
    public RecommendationRunStage? Stage { get; set; }

    /// <summary>
    /// How much of a counted <see cref="Stage"/> is done ("12 of 40"); <c>null</c> for a stage that isn't counted, and
    /// once the run has ended. Written when the stage begins, at its end, and in between at most every two seconds
    /// (<c>RecommendationRunner.ProgressInterval</c>): the page polls about that often, and a write per catalog call
    /// would put a database write behind each of a large library's thousands.
    /// </summary>
    public int? StageDone { get; set; }

    /// <summary>What a counted <see cref="Stage"/> has to do in all; <c>null</c> with <see cref="StageDone"/>.</summary>
    public int? StageTotal { get; set; }

    /// <summary>How many catalog works of the library it started from; <c>null</c> unless it succeeded.</summary>
    public int? SeedCount { get; set; }

    /// <summary>How many works it recommends, for every user together; <c>null</c> unless it succeeded.</summary>
    public int? CandidateCount { get; set; }

    /// <summary>Why the run failed, as a message the Api renders (ADR 0042), the catalog's secrets masked in its values;
    /// <c>null</c> unless it failed.</summary>
    [MaxLength(ErrorMaximumLength)]
    public Message? Error { get; set; }
}
