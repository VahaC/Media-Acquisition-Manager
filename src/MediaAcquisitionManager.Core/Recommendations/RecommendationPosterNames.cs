using System.Text.RegularExpressions;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — which file names the app serves a recommended work's poster under: the catalog's poster path
/// (<c>/abc.jpg</c>) without its slash. The list hands out a URL only for such a name, and the poster endpoint
/// serves nothing else, so no request can name a path outside the posters' folder.
/// </summary>
public static partial class RecommendationPosterNames
{
    /// <summary>
    /// Whether <paramref name="fileName"/> is a poster's file name: letters, digits, <c>_</c> and <c>-</c>, then a JPEG
    /// or PNG extension — the TMDb adapter's poster path without its slash, so a name the endpoint takes is always one
    /// the adapter would fetch. No dot segment, separator or drive can pass.
    /// </summary>
    public static bool IsFileName(string? fileName) => fileName is not null && FileNamePattern().IsMatch(fileName);

    /// <summary>The file name of <paramref name="posterPath"/> (<c>/abc.jpg</c> → <c>abc.jpg</c>); <c>null</c> for no
    /// path, or one the endpoint wouldn't serve.</summary>
    public static string? FileNameOf(string? posterPath) =>
        posterPath is ['/', .. var name] && IsFileName(name) ? name : null;

    /// <summary>The media type the extension of <paramref name="fileName"/>, an <see cref="IsFileName"/> one, names:
    /// the only two the adapter keeps.</summary>
    public static string MediaTypeOf(string fileName) =>
        fileName.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg";

    /// <summary><c>\z</c>, not <c>$</c>, which would also accept a trailing line break.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,100}\.(jpg|jpeg|png)\z")]
    private static partial Regex FileNamePattern();
}
