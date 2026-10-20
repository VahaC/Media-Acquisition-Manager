using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-34 — one preparation of the TMDb suggestions for the library works without a TMDb id: queued after a library
/// sync, when a catalog is connected, or by the owner, worked through in the background, and polled by the TMDb page.
/// <see cref="AuditableEntity.CreatedUtc"/> is when it was queued.
/// </summary>
/// <remarks>
/// The <see cref="RecommendationRunEntity"/> shape: at most one queued and one running at a time; a trigger that finds
/// one queued joins it. History, not state: the requester's name is copied onto the row (the audit-row pattern). A run
/// stores each work's candidates as it goes, so one that fails keeps the works it finished.
/// </remarks>
public class LibraryLinkSuggestionRunEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> the column holds; a longer message is fitted into it (ADR 0042).</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>Where the run stands.</summary>
    public LibraryLinkSuggestionRunStatus Status { get; set; }

    /// <summary>What queued it.</summary>
    public LibraryLinkSuggestionRunTrigger Trigger { get; set; }

    /// <summary>The user who asked for it; <c>null</c> when the app queued it, or once that user is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>The requester's user name at the time; <c>null</c> when nobody asked for it.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? RequestedByUserName { get; set; }

    /// <summary>When the worker took it; <c>null</c> while it is queued.</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>When it succeeded or failed; <c>null</c> until then.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>How many works it has to search; <c>null</c> until it has counted them.</summary>
    public int? WorksTotal { get; set; }

    /// <summary>How many of them it has searched so far.</summary>
    public int WorksDone { get; set; }

    /// <summary>Why the run failed, as a message the Api renders (ADR 0042), the catalog's secrets masked in its values;
    /// <c>null</c> unless it failed.</summary>
    [MaxLength(ErrorMaximumLength)]
    public Message? Error { get; set; }
}
