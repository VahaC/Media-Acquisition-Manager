namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>One page of a library's items.</summary>
/// <param name="Items">The items on the page.</param>
/// <param name="TotalCount">How many items the library has in all.</param>
public sealed record LibraryItemPage(IReadOnlyList<LibraryItem> Items, int TotalCount);
