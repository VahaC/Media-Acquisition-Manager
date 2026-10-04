using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>Which page of which list of a library's items to read.</summary>
/// <param name="Kind">The list: the library's movies, its series or its episodes (MAM-32), each read and
/// counted on its own.</param>
/// <param name="StartIndex">The 0-based index of the first item.</param>
/// <param name="Limit">The most items to return.</param>
public sealed record LibraryPageRequest(MediaWorkKind Kind, int StartIndex, int Limit);
