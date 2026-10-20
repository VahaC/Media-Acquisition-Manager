using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// How one release source answered one stored search (MAM-12): asked and succeeded, failed with a
/// status, or not asked and why. The releases it listed point at this row.
/// </summary>
/// <remarks>
/// <para>
/// History, not state: the integration's name is copied onto the row (the audit-row pattern), so the
/// search still says which source answered after the integration is renamed or removed, which sets
/// <see cref="IntegrationConnectionId"/> null.
/// </para>
/// <para>
/// No <c>CreatedUtc</c>, for the <see cref="LibraryLocationFolderEntity"/> reason: a source row is part
/// of its search, written with it, and the search already says when.
/// </para>
/// </remarks>
public class SearchSourceEntity : BaseEntity
{
    /// <summary>The longest <see cref="Error"/> the column holds. MAM-47 (ADR 0042) — the message as JSON, fitted into it
    /// by cutting its longest value (<c>MessageFit</c>).</summary>
    public const int ErrorMaximumLength = 1000;

    /// <summary>The search; the row is removed with it.</summary>
    public Guid SearchId { get; set; }

    /// <summary>The release source's integration; <c>null</c> once it is removed.</summary>
    public Guid? IntegrationConnectionId { get; set; }

    /// <summary>The integration's name when it was searched.</summary>
    [Required, MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string Name { get; set; } = default!;

    /// <summary>The 0-based place of the source in the search: the sources in name order when it was
    /// made. The first tie-break of every order of the search's releases.</summary>
    public int Position { get; set; }

    /// <summary>How the call to it went (<c>Succeeded</c> when it answered, even with nothing);
    /// <c>null</c> when the app didn't ask it, and <see cref="NotSearchedReason"/> says why.</summary>
    public IntegrationCallStatus? Status { get; set; }

    /// <summary>Why the app didn't ask it; <c>null</c> when it did.</summary>
    public ReleaseSourceNotSearchedReason? NotSearchedReason { get; set; }

    /// <summary>
    /// What went wrong, with every secret masked, as a message the Api renders in the reader's language (ADR 0042);
    /// <c>null</c> when nothing did. It can name the service's address, so only the installation owner reads it (the
    /// MAM-8 rule for a sync run's error).
    /// </summary>
    [MaxLength(ErrorMaximumLength)]
    public Message? Error { get; set; }
}
