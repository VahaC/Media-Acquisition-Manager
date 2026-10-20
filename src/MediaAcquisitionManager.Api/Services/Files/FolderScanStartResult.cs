using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>The outcome of <see cref="IFolderScanService.StartAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="FolderScanStartStatus.Queued"/>, as the owner sees it.</param>
/// <param name="Error">Why it wasn't queued, as a message the Api renders (ADR 0042).</param>
public sealed record FolderScanStartResult(FolderScanStartStatus Status, FolderScanRunDto? Run, Message? Error)
{
    public static FolderScanStartResult Queued(FolderScanRunDto run) => new(FolderScanStartStatus.Queued, run, null);

    public static FolderScanStartResult NoAllowedRoots(Message error) => new(FolderScanStartStatus.NoAllowedRoots, null, error);

    public static FolderScanStartResult Unavailable(Message error) => new(FolderScanStartStatus.Unavailable, null, error);
}
