using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>MAM-42 — the outcome of <see cref="IUnreportedFileService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="UnreportedFileListStatus.Listed"/>.</param>
/// <param name="Error">What the list doesn't take otherwise, as a message the Api renders (ADR 0042).</param>
public sealed record UnreportedFileListResult(UnreportedFileListStatus Status, UnreportedFileListResponse? List, Message? Error)
{
    public static UnreportedFileListResult Listed(UnreportedFileListResponse list) => new(UnreportedFileListStatus.Listed, list, null);

    public static UnreportedFileListResult Invalid(Message error) => new(UnreportedFileListStatus.Invalid, null, error);
}
