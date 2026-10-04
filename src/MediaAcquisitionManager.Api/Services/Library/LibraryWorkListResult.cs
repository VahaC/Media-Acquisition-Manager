using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>The outcome of <see cref="ILibraryService.ListWorksAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="LibraryWorkListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take otherwise.</param>
public sealed record LibraryWorkListResult(LibraryWorkListStatus Status, LibraryWorkListResponse? List, string? Error)
{
    public static LibraryWorkListResult Listed(LibraryWorkListResponse list) => new(LibraryWorkListStatus.Listed, list, null);

    public static LibraryWorkListResult Invalid(string error) => new(LibraryWorkListStatus.Invalid, null, error);

    public static LibraryWorkListResult NotFound { get; } = new(LibraryWorkListStatus.NotFound, null, null);
}
