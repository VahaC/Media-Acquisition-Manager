using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one page of the confident suggestions (<see cref="ILibraryLinkSuggestionService.ListConfidentAsync"/>),
/// or why not.</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> or <see cref="LibraryLinkStatus.Invalid"/>.</param>
/// <param name="List">The page when listed.</param>
/// <param name="Error">Why a library or a page isn't taken, as a message the Api renders (ADR 0042).</param>
public sealed record ConfidentSuggestionListResult(LibraryLinkStatus Status, ConfidentSuggestionListResponse? List, Message? Error)
{
    public static ConfidentSuggestionListResult Listed(ConfidentSuggestionListResponse list) => new(LibraryLinkStatus.Succeeded, list, null);

    public static ConfidentSuggestionListResult Invalid(Message error) => new(LibraryLinkStatus.Invalid, null, error);
}
