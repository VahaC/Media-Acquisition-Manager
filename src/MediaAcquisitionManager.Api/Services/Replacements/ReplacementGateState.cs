using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the owner's replacement gates as the database holds them right now (ADR 0033): the master switch, the
/// allowed roots, the per-folder ticks and the library folders they are read against.
/// </summary>
/// <param name="IsEnabled">Whether the owner switched replacement on; <c>false</c> while no switch row exists.</param>
/// <param name="AllowedRoots">Every allowed root in canonical form, in the order the owner entered them.</param>
/// <param name="Ticks">Every stored tick as stored, in the order last saved, whatever it still means.</param>
/// <param name="Folders">Every library folder the mappings bring into the app, inside the allowed roots or not, by app
/// path (ordinal).</param>
/// <param name="StaleTicks">The stored ticks that are no longer any of <paramref name="Folders"/>, as stored: kept until
/// the owner unticks them, with no effect.</param>
/// <param name="UnmappedLibraryFolderCount">How many stored library folders the mappings give no app path, counted once
/// per library that lists them; none of them can be ticked.</param>
public sealed record ReplacementGateState(
    bool IsEnabled,
    IReadOnlyList<string> AllowedRoots,
    IReadOnlyList<string> Ticks,
    IReadOnlyList<ReplacementGateFolder> Folders,
    IReadOnlyList<string> StaleTicks,
    int UnmappedLibraryFolderCount)
{
    /// <summary>The folders whose tick counts: ticked, a library folder the app knows and inside an allowed
    /// root.</summary>
    public IReadOnlyList<ReplacementGateFolder> EffectiveFolders => [.. Folders.Where(folder => folder.IsEffective)];

    /// <summary>
    /// Whether a file at <paramref name="appPath"/> lies in a ticked folder: some library folder holds it, and every
    /// library folder that holds it is one of <see cref="EffectiveFolders"/>. The switch is not part of the answer.
    /// </summary>
    /// <remarks>
    /// MAM-24 — every folder, not any: with <c>/media/movies</c> ticked and a library at <c>/media/movies/kids</c> left
    /// unticked, a copy in <c>kids</c> must stay out of reach, as the page shows its own folder unticked (ADR 0033 gates
    /// on "the library folder that holds the old copy"). A holding folder outside the allowed roots can't be ticked, so a
    /// copy it holds stays out of reach too: fail closed.
    /// </remarks>
    public bool IsInTickedFolder(NormalizedPath appPath)
    {
        var holding = Folders.Where(folder => appPath.IsUnder(folder.AppPath)).ToList();
        return holding.Count > 0 && holding.All(folder => folder.IsEffective);
    }
}
