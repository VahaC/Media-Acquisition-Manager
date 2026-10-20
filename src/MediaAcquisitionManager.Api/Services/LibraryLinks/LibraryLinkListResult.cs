using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one page of the TMDb page's links (<see cref="ILibraryLinkService.ListAsync"/>), or why not.</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> or <see cref="LibraryLinkStatus.Invalid"/>.</param>
/// <param name="List">The page when listed.</param>
/// <param name="Error">Why a filter or a page isn't taken, as a message the Api renders (ADR 0042).</param>
public sealed record LibraryLinkListResult(LibraryLinkStatus Status, LibraryLinkListResponse? List, Message? Error)
{
    public static LibraryLinkListResult Listed(LibraryLinkListResponse list) => new(LibraryLinkStatus.Succeeded, list, null);

    public static LibraryLinkListResult Invalid(Message error) => new(LibraryLinkStatus.Invalid, null, error);
}
