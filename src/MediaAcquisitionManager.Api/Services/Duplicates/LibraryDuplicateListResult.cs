using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-20 — the outcome of <see cref="ILibraryDuplicateService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="LibraryDuplicateListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take otherwise.</param>
public sealed record LibraryDuplicateListResult(LibraryDuplicateListStatus Status, LibraryDuplicateListResponse? List, string? Error)
{
    public static LibraryDuplicateListResult Listed(LibraryDuplicateListResponse list) => new(LibraryDuplicateListStatus.Listed, list, null);

    public static LibraryDuplicateListResult Invalid(string error) => new(LibraryDuplicateListStatus.Invalid, null, error);
}
