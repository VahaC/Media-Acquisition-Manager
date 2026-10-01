namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One version of a work as a library reported it, with its files.</summary>
/// <param name="Id">The version's id; the same across syncs while the library reports it.</param>
/// <param name="SourceName">The name of the integration that reported it; <c>null</c> when none did.</param>
/// <param name="SourceItemId">The library's own id of the item it belongs to.</param>
/// <param name="EditionLabel">The library's label for it when an item offers a choice of versions;
/// <c>null</c> otherwise.</param>
/// <param name="ObservedUtc">When a sync last saw it.</param>
/// <param name="Files">Its files, by path.</param>
public sealed record LibraryVersionDto(
    Guid Id,
    string? SourceName,
    string? SourceItemId,
    string? EditionLabel,
    DateTime ObservedUtc,
    IReadOnlyList<LibraryFileDto> Files);
