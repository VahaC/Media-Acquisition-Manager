namespace MediaAcquisitionManager.Core.Enums;

/// <summary>What kind of row an audit event's target is. Stored by name.</summary>
public enum AuditTargetKind
{
    /// <summary>A user account.</summary>
    User = 1,

    /// <summary>An integration connection (MAM-7).</summary>
    Integration = 2,

    /// <summary>A destination of a download client (MAM-11).</summary>
    Destination = 3,

    /// <summary>A download job: a release sent to a download client (MAM-13).</summary>
    DownloadJob = 4,

    /// <summary>MAM-34 — the owner's TMDb link of a library item, named by its work's title.</summary>
    LibraryLink = 5,

    /// <summary>
    /// MAM-21 — a possible match of the Duplicates list, by its group's key, named by the title and year it was offered
    /// by.
    /// </summary>
    DuplicateMatch = 6,

    /// <summary>MAM-24 — a scheduled replacement of old copies, named by the release that brings the new one.</summary>
    ReplacementJob = 7,

    /// <summary>MAM-26 — one name a replacement moves into the quarantine, restores or deletes (its
    /// <c>ReplacementJobNames</c> row), named by the file's own name.</summary>
    ReplacementJobName = 8,

    /// <summary>MAM-27 — one old copy the owner ticked for a replacement (its <c>ReplacementJobFiles</c> row), named by the
    /// file's own name.</summary>
    ReplacementJobFile = 9,

    /// <summary>MAM-40 — the owner's removal of one copy of a duplicate group (its <c>CopyRemovals</c> row), named by the
    /// group's heading.</summary>
    CopyRemoval = 10,

    /// <summary>MAM-40 — one name a removal moves into the quarantine, restores or deletes (its <c>CopyRemovalNames</c>
    /// row), named by the file's own name.</summary>
    CopyRemovalName = 11,

    /// <summary>MAM-42 — a name the scan of the media folders stored that no library reported (its <c>PhysicalFiles</c>
    /// row), named by the file's own name.</summary>
    UnreportedFile = 12,

    /// <summary>MAM-5 — a user's personal access token (its <c>PersonalAccessTokens</c> row), named by its name.</summary>
    PersonalAccessToken = 13,
}
