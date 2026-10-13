using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>
/// MAM-43 — the names by which Jellyfin takes a video for a film's extra rather than a film of its own, so a library
/// never lists it: Emby.Naming's <c>NamingOptions.VideoExtraRules</c> for video, as <c>ExtraRuleResolver</c> applies them.
/// What tells the owner why a file in a library's folders is in no library (the Files in no library list).
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of rule. A folder name, compared with the file's own folder only, letter case ignored, and never when that
/// folder is the library's root. A file name, the name without its extension and with its trailing digits trimmed equal
/// to <c>trailer</c> or <c>sample</c>. A suffix that name ends with. <c>theme-music</c> and <c>theme</c> are audio rules
/// and name no video.
/// </para>
/// <para>
/// Only ever an explanation: which work a file is stays the library's word (ADR 0017), so nothing is grouped, counted or
/// hidden by these names.
/// </para>
/// </remarks>
public static class LibraryExtraNames
{
    private static readonly string[] FolderNames =
    [
        "trailers", "backdrops", "behind the scenes", "deleted scenes", "interviews", "scenes", "samples", "sample", "shorts",
        "featurettes", "extras", "extra", "other", "clips",
    ];

    private static readonly string[] FileNames = ["trailer", "sample"];

    private static readonly string[] Suffixes =
    [
        "-trailer", ".trailer", "_trailer", "- trailer", "-sample", ".sample", "_sample", "- sample", "-scene", "-clip",
        "-interview", "-behindthescenes", "-deleted", "-deletedscene", "-featurette", "-short", "-extra", "-other",
    ];

    private static readonly char[] Digits = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9'];

    /// <summary>
    /// The name of <paramref name="file"/>'s own folder, as it is on the disk ("Shorts"), when it is an extras folder's
    /// name; <c>null</c> when it isn't, or the folder is <paramref name="libraryFolder"/> itself — the library's root,
    /// where Jellyfin doesn't apply the rule.
    /// </summary>
    public static string? ExtrasFolderOf(NormalizedPath file, NormalizedPath libraryFolder)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(libraryFolder);
        if (file.Segments.Count < 2 || (file.Segments.Count - 1 == libraryFolder.Segments.Count && file.IsUnder(libraryFolder)))
            return null;

        var folder = file.Segments[^2];
        return FolderNames.Contains(folder, StringComparer.OrdinalIgnoreCase) ? folder : null;
    }

    /// <summary>
    /// The rule <paramref name="fileName"/>, a file's own name, matches as an extra's name — <c>trailer</c> or
    /// <c>sample</c> for the whole name, else the suffix it ends with ("-trailer") — in the rule's own letters;
    /// <c>null</c> when it matches none.
    /// </summary>
    public static string? ExtraNameOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var dot = fileName.LastIndexOf('.');
        var stem = (dot >= 0 ? fileName[..dot] : fileName).TrimEnd(Digits);
        return FileNames.FirstOrDefault(name => stem.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Suffixes.FirstOrDefault(suffix => stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
