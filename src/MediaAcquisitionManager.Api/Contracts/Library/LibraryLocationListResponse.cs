namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The libraries the Library page filters by; never paged, as a server has a handful.</summary>
/// <param name="Items">Every library that holds at least one file, ordered by name in Ukrainian
/// alphabetical order, letter case ignored, then by source name.</param>
public sealed record LibraryLocationListResponse(IReadOnlyList<LibraryLocationDto> Items);
