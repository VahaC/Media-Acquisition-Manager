using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>The outcome of <see cref="IFileInspectionService.StartAsync"/> and
/// <see cref="IFileInspectionService.EnsureQueuedAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="FileInspectionStartStatus.Queued"/>, as the owner
/// sees it.</param>
/// <param name="Error">The sentence when it wasn't queued.</param>
public sealed record FileInspectionStartResult(FileInspectionStartStatus Status, FileInspectionRunDto? Run, string? Error)
{
    public static FileInspectionStartResult Queued(FileInspectionRunDto run) => new(FileInspectionStartStatus.Queued, run, null);

    public static FileInspectionStartResult NoAllowedRoots(string error) => new(FileInspectionStartStatus.NoAllowedRoots, null, error);

    public static FileInspectionStartResult Unavailable(string error) => new(FileInspectionStartStatus.Unavailable, null, error);
}
