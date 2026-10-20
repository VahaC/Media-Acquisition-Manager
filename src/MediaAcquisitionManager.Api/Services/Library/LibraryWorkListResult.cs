using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>The outcome of <see cref="ILibraryService.ListWorksAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="LibraryWorkListStatus.Listed"/>.</param>
/// <param name="Error">What the list doesn't take otherwise, as a message the Api renders (ADR 0042).</param>
public sealed record LibraryWorkListResult(LibraryWorkListStatus Status, LibraryWorkListResponse? List, Message? Error)
{
    public static LibraryWorkListResult Listed(LibraryWorkListResponse list) => new(LibraryWorkListStatus.Listed, list, null);

    public static LibraryWorkListResult Invalid(Message error) => new(LibraryWorkListStatus.Invalid, null, error);

    public static LibraryWorkListResult NotFound { get; } = new(LibraryWorkListStatus.NotFound, null, null);
}
