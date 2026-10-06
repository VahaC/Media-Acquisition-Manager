namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — "Accept all confident": the ticked confident suggestions of the page shown, each linked and
/// written into the library in the background.</summary>
/// <remarks>
/// The items nullable, for the <see cref="LibraryLinkRequest"/> reason (R18); at most 100 (R20), the most a page shows.
/// A missing list is an empty one.
/// </remarks>
/// <param name="Items">The works and the suggestion the owner accepts for each.</param>
public sealed record LibraryLinkAcceptRequest(IReadOnlyList<LibraryLinkAcceptItemRequest?>? Items = null);
