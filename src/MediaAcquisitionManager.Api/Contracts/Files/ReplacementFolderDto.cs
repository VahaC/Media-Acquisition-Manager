using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — one library folder on the Media folders page's replacement list, with its tick (ADR 0033).</summary>
/// <param name="AppPath">The folder inside the app, in canonical form; what the page sends back to keep it
/// ticked.</param>
/// <param name="LibraryNames">Every library that lists the folder ("Фільми"); none for
/// <see cref="ReplacementFolderStatus.NoLongerALibraryFolder"/>.</param>
/// <param name="Ticked">Whether the owner ticked it for replacement.</param>
/// <param name="Status">Whether a tick here counts, and why not.</param>
public sealed record ReplacementFolderDto(string AppPath, IReadOnlyList<string> LibraryNames, bool Ticked, ReplacementFolderStatus Status);
