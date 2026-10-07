using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// What the name of a file no library reported claims to be (<see cref="ReleaseWorkClaims.Read"/>): its file name
/// without the extension, then its folder's name — "Aliens (1986)/aliens.mkv" names its work by the folder. A name that
/// names no year claims nothing ("No year, no title").
/// </summary>
/// <remarks>
/// <para>
/// MAM-33 — a scan-found file keeps a work out of the recommendations by its name's title and year.
/// </para>
/// <para>
/// MAM-20 — moved here from the recommendations, so the Duplicates list offers a scan-found file as a possible match of
/// a work by the same reading (ADR 0012's title and year, never a title alone). Moved with one fix: a backslash now
/// separates folders as a slash does, so a Windows-style scan path ("D:\Movies\Aliens (1986)\aliens.mkv") claims by its
/// folder's name in the recommendations too, where it used to claim nothing.
/// </para>
/// </remarks>
public static class FileNameClaims
{
    /// <summary>The claims of the file at <paramref name="path"/> (either separator): its file name's, then its
    /// folder's; empty when neither names a year.</summary>
    public static IReadOnlyList<WorkClaim> Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        List<WorkClaim> claims = [];
        foreach (var name in NamesOf(path))
        {
            var read = ReleaseWorkClaims.Read(name);
            if (read.Titles.Count > 0 && read.Year is not null)
                claims.Add(new WorkClaim(read.Titles, ClaimedYears.Exactly(read.Year), []));
        }

        return claims;
    }

    /// <summary>
    /// MAM-20 — <see cref="Read"/>'s claims of a file that can be one work of its title and year: none when its file name
    /// or its folder's name names seasons or episodes (<see cref="ReleaseWorkClaims.ClaimsSeasonsOrEpisodes"/>).
    /// </summary>
    /// <remarks>
    /// Every episode of a show claims the show's one title and year — "Severance (2022) - S01E01 - Good News About
    /// Hell.mkv" and its eight siblings, the files of "Season 01" or of a season pack's folder — so read by title and
    /// year, nine episodes would be offered as one work. The cost: a film whose name holds an episode marker, "Star Wars
    /// Episode 1 (1999).mkv", is offered as no possible match by its name; its bytes still group it (ADR 0028).
    /// </remarks>
    public static IReadOnlyList<WorkClaim> ReadUnlessEpisode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return NamesOf(path).Any(ReleaseWorkClaims.ClaimsSeasonsOrEpisodes) ? [] : Read(path);
    }

    // The file name without its extension, then its folder's name when it has one.
    private static IEnumerable<string> NamesOf(string path)
    {
        // An array, not two chars: Split('/', '\\', options) binds to Split(char, int count, options) and never splits
        // on a backslash (the bug the recommendations carried before MAM-20 moved this here).
        var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            yield break;

        yield return Path.GetFileNameWithoutExtension(parts[^1]);
        if (parts.Length > 1)
            yield return parts[^2];
    }
}
