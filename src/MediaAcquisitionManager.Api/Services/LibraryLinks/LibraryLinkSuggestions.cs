using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — what a library work's TMDb dialog shows of its prepared suggestions
/// (<see cref="ILibraryLinkSuggestionReader"/>).</summary>
/// <param name="Suggestions">The candidates to pick from, best first; none while none were prepared.</param>
/// <param name="Search">Where their preparation stands.</param>
public sealed record LibraryLinkSuggestions(IReadOnlyList<LinkCandidateDto> Suggestions, LibraryLinkSearchStateDto Search);
