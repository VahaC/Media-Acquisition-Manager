using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Accounts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// One sync of a media library integration's snapshot (MAM-8): queued by the owner, read in the
/// background, and polled by the Library page. <see cref="AuditableEntity.CreatedUtc"/> is when it was
/// requested.
/// </summary>
/// <remarks>
/// History, not state: the integration's and the requester's names are copied onto the row (the
/// audit-row pattern), because a run must still say what it was after either is removed, which sets
/// its foreign key null. It is also why a sync writes no audit row of its own: the run records who
/// started it.
/// </remarks>
public class LibrarySyncRunEntity : AuditableEntity
{
    /// <summary>The longest <see cref="Error"/> the column holds; a longer sentence is cut to it
    /// (<c>LibrarySyncRunUpdates</c>).</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>The integration synced; <c>null</c> once it is removed.</summary>
    public Guid? IntegrationConnectionId { get; set; }

    /// <summary>The integration's name when the sync was requested.</summary>
    [Required, MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string IntegrationName { get; set; } = default!;

    /// <summary>The user who started it; <c>null</c> once that user is deleted.</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>The requester's user name at the time.</summary>
    [MaxLength(UserNameRules.MaximumLength)]
    public string? RequestedByUserName { get; set; }

    /// <summary>Where the sync stands.</summary>
    public LibrarySyncStatus Status { get; set; }

    /// <summary>When the worker took it; <c>null</c> while it is queued.</summary>
    public DateTime? StartedUtc { get; set; }

    /// <summary>When it succeeded or failed; <c>null</c> until then.</summary>
    public DateTime? FinishedUtc { get; set; }

    /// <summary>How many different items have been read so far: the progress the page shows.</summary>
    public int ItemsRead { get; set; }

    /// <summary>How many items the library reported in all; <c>null</c> until the first page.</summary>
    public int? ItemsTotal { get; set; }

    /// <summary>How many works the snapshot holds; <c>null</c> unless the sync succeeded.</summary>
    public int? WorksCount { get; set; }

    /// <summary>How many versions the snapshot holds; <c>null</c> unless the sync succeeded.</summary>
    public int? VersionsCount { get; set; }

    /// <summary>How many file names the snapshot holds; <c>null</c> unless the sync succeeded.</summary>
    public int? FilesCount { get; set; }

    /// <summary>A readable sentence saying why the sync failed, with every secret masked; <c>null</c>
    /// unless it failed.</summary>
    [MaxLength(ErrorMaximumLength)]
    public string? Error { get; set; }
}
