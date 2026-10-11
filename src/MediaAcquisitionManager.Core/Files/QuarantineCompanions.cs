using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// MAM-26, ADR 0030 "What" — which files beside an old video move into the quarantine with it: its own companions,
/// named after it, and nothing that belongs to another video in the same folder.
/// </summary>
/// <remarks>
/// <para>
/// A companion's name is the video's name without its extension followed by a dot — <c>Alien.srt</c>,
/// <c>Alien.uk.srt</c> and <c>Alien.nfo</c> beside <c>Alien.mkv</c> — and is no media file itself. qBittorrent saves a
/// single-file torrent straight into the library folder, so one folder may hold many films: beside
/// <c>Alien (1979).mkv</c> and <c>Alien (1979).2160p.mkv</c>, <c>Alien (1979).2160p.uk.srt</c> begins with the longer
/// name of the second video followed by a dot, so it belongs to that one and stays.
/// </para>
/// <para>
/// Letter case follows the folder's path style, as everywhere else the app compares names.
/// </para>
/// </remarks>
public static class QuarantineCompanions
{
    /// <summary>
    /// The names among <paramref name="folderFileNames"/> — every regular file in the folder, the video included —
    /// that are <paramref name="videoName"/>'s own companions, in the order given.
    /// </summary>
    /// <param name="videoName">The old video's own name, without its folder.</param>
    /// <param name="folderFileNames">The names of the regular files in its folder.</param>
    /// <param name="style">The folder's path style, which says whether letter case counts.</param>
    public static IReadOnlyList<string> Pick(string videoName, IReadOnlyList<string> folderFileNames, PathStyle style)
    {
        var comparison = style == PathStyle.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = StemOf(videoName) + ".";
        var longerPrefixes = folderFileNames
            .Where(name => MediaFileNames.IsMediaFile(name) && !string.Equals(name, videoName, comparison))
            .Select(name => StemOf(name) + ".")
            .Where(other => other.Length > prefix.Length && other.StartsWith(prefix, comparison))
            .ToList();

        return
        [
            .. folderFileNames.Where(name =>
                !MediaFileNames.IsMediaFile(name)
                && name.Length > prefix.Length
                && name.StartsWith(prefix, comparison)
                && !longerPrefixes.Any(other => name.StartsWith(other, comparison))),
        ];
    }

    /// <summary>A name without its last extension: <c>Alien (1979)</c> of <c>Alien (1979).mkv</c>.</summary>
    private static string StemOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
