namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Replaces the allowed roots (MAM-9): the whole list, saved by the settings page's one Save.</summary>
/// <remarks>
/// MAM-9 — the items are nullable for the <see cref="PathMappingRequest"/> reason: a blank or missing
/// path must reach the allowed-root rules, whose sentence the page shows, not stop at a validation body
/// with no <c>error</c> in it. A missing list is an empty one.
/// </remarks>
/// <param name="Paths">The folders as the owner typed them; none to allow nothing.</param>
public sealed record UpdateAllowedRootsRequest(IReadOnlyList<string?>? Paths);
