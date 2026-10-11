namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The folders inside the app where it may read files (MAM-9), behind the Media folders
/// settings page.</summary>
/// <remarks>
/// MAM-24 — and the owner's replacement gates the same page saves with them (ADR 0033): the master switch and the
/// library folders each with its tick. MAM-26 — and the room each quarantine takes (ADR 0030).
/// </remarks>
/// <param name="Paths">Every allowed root in canonical form, in the order the owner entered them; none
/// until the owner names one.</param>
/// <param name="ReplacementEnabled">Whether the owner switched replacement on; off by default.</param>
/// <param name="LibraryFolders">Every library folder the mappings bring into the app, then every stored tick that is no
/// longer one, by app path (ordinal); none until a library is synced.</param>
/// <param name="UnmappedLibraryFolderCount">How many stored library folders the mappings give no app path, so they
/// can't be listed or ticked; counted once per library that lists them.</param>
/// <param name="Quarantines">MAM-26 — every quarantine holding a replacement's names now, with the room it takes; none
/// while no name is in one.</param>
public sealed record AllowedRootsResponse(
    IReadOnlyList<string> Paths,
    bool ReplacementEnabled,
    IReadOnlyList<ReplacementFolderDto> LibraryFolders,
    int UnmappedLibraryFolderCount,
    IReadOnlyList<QuarantineSpaceDto> Quarantines);
