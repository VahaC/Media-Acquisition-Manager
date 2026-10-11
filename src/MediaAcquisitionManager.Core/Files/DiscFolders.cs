using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// MAM-26, ADR 0030 — a clip of a Blu-ray or DVD folder (<c>BDMV/STREAM/00000.m2ts</c>, <c>VIDEO_TS/VTS_01_1.VOB</c>):
/// the app knows one clip of a disc, and moving it alone would break the disc, so such a copy is never moved one file
/// at a time and its replacement stops for review.
/// </summary>
public static class DiscFolders
{
    private static readonly string[] Names = ["BDMV", "VIDEO_TS"];

    /// <summary>Whether <paramref name="appPath"/> lies inside a <c>BDMV</c> or <c>VIDEO_TS</c> folder, letter case
    /// ignored: discs are authored in upper case, and copied ones keep it on any file system.</summary>
    public static bool HoldsClip(NormalizedPath appPath) =>
        appPath.Segments.SkipLast(1).Any(segment => Names.Contains(segment, StringComparer.OrdinalIgnoreCase));
}
