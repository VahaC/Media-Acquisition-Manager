namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// Which file names the scan of the allowed roots takes as media files (MAM-18, ADR 0021): a name ending in
/// a video container's extension, letter case ignored (Vasyl's decision).
/// </summary>
/// <remarks>
/// <para>
/// An extension decides only whether the app looks at a file at all, never what the file is: which work
/// it belongs to, or whether it is a movie or an episode, stays the library's word (ADR 0017). Reading
/// every file with the media inspector instead would start it thousands of times on posters, subtitles
/// and <c>.nfo</c> files at the first scan.
/// </para>
/// <para>
/// The clips inside a Blu-ray or DVD folder (<c>BDMV/STREAM/*.m2ts</c>, <c>VIDEO_TS/*.vob</c>) count like any
/// other file, and so does a disc image (<c>.iso</c>), which the media inspector doesn't read and so shows
/// as unreadable: they take space on the disk like any other copy.
/// </para>
/// </remarks>
public static class MediaFileNames
{
    /// <summary>The extensions of a media file, without the dot, in lower case.</summary>
    public static IReadOnlyList<string> VideoExtensions { get; } =
    [
        "mkv", "mp4", "m4v", "avi", "mov", "ts", "m2ts", "mts", "mpg", "mpeg", "vob", "wmv", "asf", "flv",
        "webm", "ogv", "rm", "rmvb", "iso",
    ];

    private static readonly HashSet<string> Extensions = new(VideoExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="fileName"/>, a file's own name without its folder, ends in a media file's
    /// extension. Only the part after the last dot counts, so <c>film.mkv.part</c>, a download not yet
    /// finished, isn't one; <c>.mkv</c> alone is a file named so, and is.
    /// </summary>
    public static bool IsMediaFile(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && Extensions.Contains(fileName[(dot + 1)..]);
    }
}
