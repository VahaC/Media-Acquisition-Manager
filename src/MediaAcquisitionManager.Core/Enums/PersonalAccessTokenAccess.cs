namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-5 — what a personal access token may do as its user (ADR 0040). Stored by name. Either level reads only what its
/// user reads; neither ever reaches settings, users, the account or a replacement.
/// </summary>
public enum PersonalAccessTokenAccess
{
    /// <summary>Reads: every GET its user can reach, plus the two searches, which are POSTs that only read.</summary>
    ReadOnly = 1,

    /// <summary>Reads, plus exactly three writes: sending a download (Keep both), cancelling following, and marking a
    /// recommendation Not interested.</summary>
    ReadAndDownload = 2,
}
