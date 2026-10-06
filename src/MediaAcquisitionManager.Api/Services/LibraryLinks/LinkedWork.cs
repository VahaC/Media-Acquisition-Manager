using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one work's ids as every reader counts them once its items' TMDb links are applied
/// (<see cref="ILibraryLinkReader"/>).</summary>
/// <param name="Identifiers">
/// The work's identifier rows with its links applied (<c>LinkedIdentifiers.Effective</c>): the library's rows, less the
/// id a link replaces while it isn't confirmed in the library, then a row per link to the work itself (its TMDb id, the
/// source a person, <c>ProvenanceSourceKind.Person</c>) and one for the IMDb id TMDb gave when the item's library holds
/// none (the source the catalog, <c>ProvenanceSourceKind.Metadata</c>). The rows a link stands for are made up for
/// reading, unsaved and untracked: a link is never a fact on the work (ADR 0008), and nothing ever saves them.
/// </param>
/// <param name="Links">The links attached to the work, the latest set first; none for a work no item of which is
/// linked.</param>
/// <param name="ConflictingValue">The library's TMDb id one of the linked items holds beside its link — neither the
/// link's nor one it replaces; for an episode's link to its show, a film id the library gave the item after it — so
/// neither wins; <c>null</c> when there is none.</param>
/// <param name="IsLinked">
/// Whether the work counts as linked: one of its items has an attached link, or — an episode — its series has one (R16:
/// an episode is known by its series' ids). A linked work misses no id (decision 9).
/// </param>
public sealed record LinkedWork(
    IReadOnlyList<MediaWorkIdentifierEntity> Identifiers,
    IReadOnlyList<LibraryItemLinkEntity> Links,
    string? ConflictingValue,
    bool IsLinked);
