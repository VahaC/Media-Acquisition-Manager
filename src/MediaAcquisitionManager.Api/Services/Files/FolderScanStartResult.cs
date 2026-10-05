using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>The outcome of <see cref="IFolderScanService.StartAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="FolderScanStartStatus.Queued"/>, as the owner sees it.</param>
/// <param name="Error">The sentence when it wasn't queued.</param>
public sealed record FolderScanStartResult(FolderScanStartStatus Status, FolderScanRunDto? Run, string? Error)
{
    public static FolderScanStartResult Queued(FolderScanRunDto run) => new(FolderScanStartStatus.Queued, run, null);

    public static FolderScanStartResult NoAllowedRoots(string error) => new(FolderScanStartStatus.NoAllowedRoots, null, error);

    public static FolderScanStartResult Unavailable(string error) => new(FolderScanStartStatus.Unavailable, null, error);
}
