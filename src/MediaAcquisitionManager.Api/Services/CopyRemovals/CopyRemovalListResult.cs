using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>MAM-40 — what the Removed copies list answered.</summary>
/// <param name="Status"><see cref="CopyRemovalStatus.Done"/>, or <see cref="CopyRemovalStatus.Invalid"/> for a page out of
/// range.</param>
/// <param name="List">The page, when done.</param>
/// <param name="Error">The sentence saying what is wrong with the page, when invalid.</param>
public sealed record CopyRemovalListResult(CopyRemovalStatus Status, CopyRemovalListResponse? List, string? Error)
{
    public static CopyRemovalListResult Listed(CopyRemovalListResponse list) => new(CopyRemovalStatus.Done, list, null);

    public static CopyRemovalListResult Invalid(string error) => new(CopyRemovalStatus.Invalid, null, error);
}
