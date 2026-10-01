namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The media library integrations the Library page syncs from; never paged, as an installation
/// has a handful.</summary>
/// <param name="Items">Every media library integration, ordered by name, letter case ignored.</param>
public sealed record LibrarySourceListResponse(IReadOnlyList<LibrarySourceDto> Items);
