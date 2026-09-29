namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>Which page of a library's items to read.</summary>
/// <param name="StartIndex">The 0-based index of the first item.</param>
/// <param name="Limit">The most items to return.</param>
public sealed record LibraryPageRequest(int StartIndex, int Limit);
