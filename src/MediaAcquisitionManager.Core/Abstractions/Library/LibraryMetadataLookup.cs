namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-34 — what a library answered <see cref="IMediaLibraryIdentifierWriter.RequestMetadataLookupAsync"/>: it took the
/// request for the item, and looks the item up in its own time. What the lookup finds is the library's to say — the
/// next sync reads it.
/// </summary>
/// <param name="SourceItemId">The library's id of the item the request was taken for.</param>
public sealed record LibraryMetadataLookup(string SourceItemId);
