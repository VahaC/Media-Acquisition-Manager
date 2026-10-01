namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The media library integrations the Library page syncs from; never paged, as an installation
/// has a handful.</summary>
/// <param name="Items">Every media library integration, ordered by name, letter case ignored.</param>
/// <param name="Inspection">Where the inspection of the library's files stands (MAM-9): one for the
/// whole library, as a run reads the files of every source.</param>
public sealed record LibrarySourceListResponse(IReadOnlyList<LibrarySourceDto> Items, FileInspectionOverviewDto Inspection);
