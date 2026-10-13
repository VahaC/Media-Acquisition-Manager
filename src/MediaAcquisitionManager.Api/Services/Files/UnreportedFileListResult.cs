using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>MAM-42 — the outcome of <see cref="IUnreportedFileService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="UnreportedFileListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take otherwise.</param>
public sealed record UnreportedFileListResult(UnreportedFileListStatus Status, UnreportedFileListResponse? List, string? Error)
{
    public static UnreportedFileListResult Listed(UnreportedFileListResponse list) => new(UnreportedFileListStatus.Listed, list, null);

    public static UnreportedFileListResult Invalid(string error) => new(UnreportedFileListStatus.Invalid, null, error);
}
