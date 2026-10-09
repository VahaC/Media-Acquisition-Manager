namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — one ticked old copy as a scheduled replacement keeps it (<c>ReplacementJobFileEntity</c>): which file of which
/// version and work, where, and the identity the disk gave it while the schedule was planned (ADR 0032).
/// </summary>
/// <param name="PhysicalFileId">The file row it was ticked as: the id the dialog listed.</param>
/// <param name="MediaVersionId">Its version.</param>
/// <param name="MediaWorkId">The film or the episode it is a copy of.</param>
/// <param name="MatchedWorkId">The film or the series the release matched.</param>
/// <param name="SeasonNumber">An episode's season as its own library item places it inside the claim; <c>null</c> for a
/// film.</param>
/// <param name="EpisodeNumber">An episode's number there; <c>null</c> for a film.</param>
/// <param name="ServicePath">Its path as its library reported it.</param>
/// <param name="AppPath">Its path inside the app, canonical.</param>
/// <param name="DeviceId"><c>st_dev</c>, fresh from the disk.</param>
/// <param name="Inode"><c>st_ino</c>, fresh from the disk.</param>
/// <param name="SizeBytes">Its size, fresh from the disk.</param>
/// <param name="ModifiedUtc">Its modification time, fresh from the disk, to the tick.</param>
/// <param name="LinkCount">Its number of names, fresh from the disk; <c>null</c> when the platform doesn't say.</param>
/// <param name="EditionLabel">Its version's own label; <c>null</c> when the library gave none.</param>
public sealed record ReplacementPlanFile(
    Guid PhysicalFileId,
    Guid MediaVersionId,
    Guid MediaWorkId,
    Guid MatchedWorkId,
    int? SeasonNumber,
    int? EpisodeNumber,
    string ServicePath,
    string AppPath,
    long DeviceId,
    long Inode,
    long SizeBytes,
    DateTime ModifiedUtc,
    int? LinkCount,
    string? EditionLabel);
