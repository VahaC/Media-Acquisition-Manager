using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — one library folder the app knows, as the replacement gates see it (ADR 0033): one entry per folder however
/// many libraries list it.
/// </summary>
/// <param name="AppPath">The folder inside the app, through its library's integration's current mappings.</param>
/// <param name="LibraryNames">Every library that lists the folder, in the order <see cref="Files.LibraryFolderReader"/>
/// reads them (by name), each name once.</param>
/// <param name="Ticked">Whether the owner ticked it for replacement.</param>
/// <param name="Status"><see cref="ReplacementFolderStatus.Available"/> inside an allowed root,
/// <see cref="ReplacementFolderStatus.OutsideMediaFolders"/> outside every one.</param>
public sealed record ReplacementGateFolder(NormalizedPath AppPath, IReadOnlyList<string> LibraryNames, bool Ticked, ReplacementFolderStatus Status)
{
    /// <summary>Whether the tick counts: ticked, and inside an allowed root (ADR 0033 — a folder that leaves the allowed
    /// roots loses its tick's effect).</summary>
    public bool IsEffective => Ticked && Status == ReplacementFolderStatus.Available;
}
