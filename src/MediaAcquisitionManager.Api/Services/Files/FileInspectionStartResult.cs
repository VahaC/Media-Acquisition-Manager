using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>The outcome of <see cref="IFileInspectionService.StartAsync"/> and
/// <see cref="IFileInspectionService.EnsureQueuedAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="FileInspectionStartStatus.Queued"/>, as the owner
/// sees it.</param>
/// <param name="Error">Why it wasn't queued, as a message the Api renders (ADR 0042).</param>
public sealed record FileInspectionStartResult(FileInspectionStartStatus Status, FileInspectionRunDto? Run, Message? Error)
{
    public static FileInspectionStartResult Queued(FileInspectionRunDto run) => new(FileInspectionStartStatus.Queued, run, null);

    public static FileInspectionStartResult NoAllowedRoots(Message error) => new(FileInspectionStartStatus.NoAllowedRoots, null, error);

    public static FileInspectionStartResult Unavailable(Message error) => new(FileInspectionStartStatus.Unavailable, null, error);
}
