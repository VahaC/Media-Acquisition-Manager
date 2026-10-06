using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one page of the confident suggestions (<see cref="ILibraryLinkSuggestionService.ListConfidentAsync"/>),
/// or why not.</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> or <see cref="LibraryLinkStatus.Invalid"/>.</param>
/// <param name="List">The page when listed.</param>
/// <param name="Error">The sentence for a library or a page the list doesn't take.</param>
public sealed record ConfidentSuggestionListResult(LibraryLinkStatus Status, ConfidentSuggestionListResponse? List, string? Error)
{
    public static ConfidentSuggestionListResult Listed(ConfidentSuggestionListResponse list) => new(LibraryLinkStatus.Succeeded, list, null);

    public static ConfidentSuggestionListResult Invalid(string error) => new(LibraryLinkStatus.Invalid, null, error);
}
