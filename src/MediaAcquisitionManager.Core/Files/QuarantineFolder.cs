using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// MAM-26, ADR 0030 — where a replaced copy waits for the owner: <c>.mam-quarantine</c> at the top of the allowed root
/// that holds it, a folder per replacement job, and below it the path the copy had inside that root, so Restore knows
/// where each name came from and two replacements of one name never meet.
/// </summary>
/// <remarks>
/// <para>
/// The quarantine sits inside the allowed root because the checks reach nothing outside one (ADR 0009) and a rename
/// stays atomic only within one mount. Jellyfin never lists it, as it skips every name that starts with a dot; the
/// app's own folder scan leaves out every folder of this name at any depth (<see cref="IsQuarantineName"/>), so a
/// quarantined file is never a file no library reported, a possible match or part of a Media folders count, whatever
/// the allowed roots become.
/// </para>
/// <para>
/// Letter case follows the path's own style, as <see cref="NormalizedPath"/> compares it: <c>.MAM-Quarantine</c> is
/// another folder on ext4 and the same one on NTFS.
/// </para>
/// </remarks>
public static class QuarantineFolder
{
    /// <summary>The quarantine folder's name.</summary>
    public const string Name = ".mam-quarantine";

    /// <summary>Whether <paramref name="name"/>, one folder's own name, is the quarantine's under
    /// <paramref name="style"/>'s rules.</summary>
    public static bool IsQuarantineName(string name, PathStyle style) =>
        string.Equals(name, Name, style == PathStyle.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Whether <paramref name="path"/> is a quarantine folder or lies inside one, at any depth.</summary>
    public static bool Holds(NormalizedPath path) => path.Segments.Any(segment => IsQuarantineName(segment, path.Style));

    /// <summary>The quarantine folder of <paramref name="allowedRoot"/>: <c>/media/.mam-quarantine</c>.</summary>
    public static NormalizedPath Of(NormalizedPath allowedRoot) => allowedRoot.Append([Name]);

    /// <summary>
    /// Where the copy at <paramref name="appPath"/>, inside <paramref name="allowedRoot"/>, waits while replacement job
    /// <paramref name="replacementJobId"/> holds it: <c>/media/.mam-quarantine/&lt;id&gt;/movies/Alien (1979)/Alien.mkv</c>
    /// for <c>/media/movies/Alien (1979)/Alien.mkv</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="appPath"/> isn't inside <paramref name="allowedRoot"/>, is
    /// the root itself, or already lies in a quarantine.</exception>
    public static NormalizedPath PathOf(NormalizedPath allowedRoot, Guid replacementJobId, NormalizedPath appPath)
    {
        var relative = appPath.SegmentsAfter(allowedRoot);
        if (relative.Count == 0)
            throw new ArgumentException($"“{appPath}” is the allowed root itself, not a file inside it.", nameof(appPath));
        if (Holds(appPath))
            throw new ArgumentException($"“{appPath}” already lies in a quarantine.", nameof(appPath));

        return Of(allowedRoot).Append([replacementJobId.ToString(), .. relative]);
    }
}
