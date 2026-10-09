namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Replaces the allowed roots (MAM-9): the whole list, saved by the settings page's one Save.</summary>
/// <remarks>
/// <para>
/// MAM-9 — the items are nullable for the <see cref="PathMappingRequest"/> reason: a blank or missing
/// path must reach the allowed-root rules, whose sentence the page shows, not stop at a validation body
/// with no <c>error</c> in it. A missing list is an empty one.
/// </para>
/// <para>
/// MAM-24 — the replacement switch and ticks travel with the roots, so the page's one Save stays one request and one
/// transaction (ADR 0033). Missing, they fail closed as a missing list does: the switch off and nothing ticked.
/// </para>
/// </remarks>
/// <param name="Paths">The folders as the owner typed them; none to allow nothing.</param>
/// <param name="ReplacementEnabled">Whether replacement is on; missing is off.</param>
/// <param name="ReplacementFolders">The library folders ticked for replacement, as the page listed them; missing is
/// none.</param>
public sealed record UpdateAllowedRootsRequest(
    IReadOnlyList<string?>? Paths,
    bool? ReplacementEnabled = null,
    IReadOnlyList<string?>? ReplacementFolders = null);
