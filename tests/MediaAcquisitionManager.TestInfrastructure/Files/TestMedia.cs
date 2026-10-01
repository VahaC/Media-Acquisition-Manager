using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// File-system and media facts for the API tests' <see cref="FakeFileInventory"/> (MAM-9): what the
/// media inspector reads from the two real ffprobe samples, track for track, and file facts with a known
/// identity. That the real parser turns the samples into exactly these is pinned in Infrastructure.Tests.
/// </summary>
public static class TestMedia
{
    /// <summary>The device all the test files live on: one ext4 filesystem, like the reference
    /// installation's. Past <c>long.MaxValue</c> as an unsigned <c>st_dev</c>, so the bit-for-bit storage
    /// is exercised too.</summary>
    public static readonly long DeviceId = unchecked((long)0x8000_0000_0000_0803UL);

    /// <summary>A modification time with sub-millisecond ticks, which a stored key must keep exactly.</summary>
    public static readonly DateTime ModifiedUtc = new DateTime(2026, 1, 31, 23, 30, 59, 123, DateTimeKind.Utc).AddTicks(4_567);

    /// <summary>The facts of one file on <see cref="DeviceId"/>; one name unless
    /// <paramref name="linkCount"/> says more.</summary>
    public static FileFacts File(long inode, long sizeBytes = 3_891_719_630, DateTime? modifiedUtc = null, long? deviceId = null, int linkCount = 1) =>
        new(sizeBytes, modifiedUtc ?? ModifiedUtc, deviceId ?? DeviceId, inode, linkCount);

    /// <summary>
    /// The Continental (2023) S01E01 (<c>VendorSamples/Ffprobe/the-continental-s01e01.json</c>): HEVC Main
    /// 10 at 1920×800 without a transfer characteristic (so its dynamic range is unknown), four audio and
    /// four subtitle tracks; the cover art is not a track.
    /// </summary>
    public static MediaFacts Continental() => new(
        TimeSpan.FromMilliseconds(5_179_168),
        6_011_343,
        [
            Video("hevc", "Main 10", 1920, 800, dynamicRange: null, language: "eng", isDefault: true),
            Audio("ac3", null, 6, "5.1(side)", "ukr", isDefault: true),
            Audio("aac", "LC", 2, "stereo", "ukr"),
            Audio("ac3", null, 2, "stereo", "ukr"),
            Audio("ac3", null, 6, "5.1(side)", "eng"),
            Subtitle("subrip", "ukr", isDefault: true, isForced: true),
            Subtitle("subrip", "ukr"),
            Subtitle("subrip", "eng"),
            Subtitle("subrip", "eng", isHearingImpaired: true),
        ]);

    /// <summary>
    /// Taken (2008) BDRemux (<c>VendorSamples/Ffprobe/taken-2008-bdremux.json</c>): H.264 High at 1920×1080
    /// in BT.709 (SDR), DTS-HD MA and AC-3 in Ukrainian and English, text and picture subtitles.
    /// </summary>
    public static MediaFacts Taken() => new(
        TimeSpan.FromMilliseconds(5_604_672),
        40_781_887,
        [
            Video("h264", "High", 1920, 1080, DynamicRange.Sdr, language: "ukr", isDefault: true, isForced: true),
            Audio("dts", "DTS-HD MA", 6, "5.1(side)", "ukr", isDefault: true, isForced: true),
            Audio("ac3", null, 6, "5.1(side)", "ukr"),
            Audio("dts", "DTS-HD MA", 6, "5.1(side)", "eng"),
            Audio("ac3", null, 6, "5.1(side)", "eng"),
            Subtitle("subrip", "ukr"),
            Subtitle("hdmv_pgs_subtitle", "ukr"),
            Subtitle("subrip", "eng"),
            Subtitle("subrip", "eng"),
            Subtitle("hdmv_pgs_subtitle", "eng"),
            Subtitle("subrip", "eng"),
        ]);

    private static MediaTrackFacts Video(
        string codec, string profile, int width, int height, DynamicRange? dynamicRange, string language, bool isDefault = false, bool isForced = false) =>
        new(MediaTrackKind.Video, codec, profile, width, height, dynamicRange, null, null, language, isDefault, isForced, false);

    private static MediaTrackFacts Audio(
        string codec, string? profile, int channels, string layout, string language, bool isDefault = false, bool isForced = false) =>
        new(MediaTrackKind.Audio, codec, profile, null, null, null, channels, layout, language, isDefault, isForced, false);

    private static MediaTrackFacts Subtitle(
        string codec, string language, bool isDefault = false, bool isForced = false, bool isHearingImpaired = false) =>
        new(MediaTrackKind.Subtitle, codec, null, null, null, null, null, null, language, isDefault, isForced, isHearingImpaired);
}
