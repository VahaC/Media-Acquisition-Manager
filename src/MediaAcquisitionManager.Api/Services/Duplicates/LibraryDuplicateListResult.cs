using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-20 — the outcome of <see cref="ILibraryDuplicateService.ListAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="LibraryDuplicateListStatus.Listed"/>.</param>
/// <param name="Error">What the list doesn't take otherwise, as a message the Api renders (MAM-47, ADR 0042).</param>
public sealed record LibraryDuplicateListResult(LibraryDuplicateListStatus Status, LibraryDuplicateListResponse? List, Message? Error)
{
    public static LibraryDuplicateListResult Listed(LibraryDuplicateListResponse list) => new(LibraryDuplicateListStatus.Listed, list, null);

    public static LibraryDuplicateListResult Invalid(Message error) => new(LibraryDuplicateListStatus.Invalid, null, error);
}
