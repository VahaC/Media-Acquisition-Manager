namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-40 — whether the owner's Remove this copy may take one file of a duplicate group into the quarantine, by what the
/// app has stored (<c>CopyRemovalRules</c>): the request checks the disk and the download clients again before anything
/// moves. Worked out for every signed-in user; the page says first that only the owner may remove.
/// </summary>
/// <param name="Allowed">Whether the stored rules let it be removed.</param>
/// <param name="Reason">Why not, one readable sentence that names no path; <c>null</c> when allowed.</param>
public sealed record CopyRemovalVerdictDto(bool Allowed, string? Reason);
