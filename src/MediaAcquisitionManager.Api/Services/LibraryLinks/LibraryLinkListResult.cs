using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one page of the TMDb page's links (<see cref="ILibraryLinkService.ListAsync"/>), or why not.</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> or <see cref="LibraryLinkStatus.Invalid"/>.</param>
/// <param name="List">The page when listed.</param>
/// <param name="Error">The sentence for a filter or a page the list doesn't take.</param>
public sealed record LibraryLinkListResult(LibraryLinkStatus Status, LibraryLinkListResponse? List, string? Error)
{
    public static LibraryLinkListResult Listed(LibraryLinkListResponse list) => new(LibraryLinkStatus.Succeeded, list, null);

    public static LibraryLinkListResult Invalid(string error) => new(LibraryLinkStatus.Invalid, null, error);
}
