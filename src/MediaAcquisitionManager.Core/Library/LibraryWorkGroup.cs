using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// Items of one library that the core takes for one work (ADR 0006, MAM-8): what
/// <see cref="LibraryWorkGrouping.Group"/> decides, before a sync gives the group a work.
/// </summary>
/// <param name="Items">The items, at least one, ordered by <see cref="LibraryItem.SourceItemId"/>
/// (ordinal).</param>
public sealed record LibraryWorkGroup(IReadOnlyList<LibraryItem> Items);
