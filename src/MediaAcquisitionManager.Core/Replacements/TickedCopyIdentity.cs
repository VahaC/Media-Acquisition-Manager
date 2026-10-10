namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — where and what one old copy a replacement holds was when it was scheduled
/// (<see cref="Entities.ReplacementJobFileEntity"/>): what a new file must never be (check 2, ADR 0035).
/// </summary>
/// <param name="AppPath">Its path inside the app, canonical.</param>
/// <param name="DeviceId">Its <c>st_dev</c>.</param>
/// <param name="Inode">Its <c>st_ino</c>.</param>
public sealed record TickedCopyIdentity(string AppPath, long DeviceId, long Inode);
