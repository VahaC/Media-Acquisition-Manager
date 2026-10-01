using System.Globalization;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// Reads the quality and languages a release's title claims (MAM-10, specification §3.1). Pure, and
/// read again whenever a release is shown, so a better reading later needs no migration.
/// </summary>
/// <remarks>
/// <para>
/// Only the part of the title after the year is read when there is one: "Interstellar (2014) BDRemux
/// 1080p Ukr/Eng | Sub Eng" reads "BDRemux 1080p Ukr/Eng | Sub Eng". A film's own name would otherwise
/// claim things: "The English Patient" English audio, "2012" or "1917" nothing worse, "4K" a resolution.
/// </para>
/// <para>
/// Languages are read from their short codes only (Ukr, Eng, Multi…), never from whole names, for the
/// same reason. Toloka writes "audio | Sub subtitles"; a language after "Sub" in its part of the title is
/// a subtitle language, any other an audio one.
/// </para>
/// </remarks>
public static partial class ReleaseTitleClaims
{
    /// <summary>The medium's names, in the order they decide: "UHD BDRemux" is a remux before it is a
    /// Blu-ray, and "Blu-ray" alone says the least.</summary>
    private static readonly (Regex Pattern, ReleaseMedium Medium)[] Media =
    [
        (RemuxPattern(), ReleaseMedium.BluRayRemux),
        (BluRayRipPattern(), ReleaseMedium.BluRayRip),
        (WebRipPattern(), ReleaseMedium.WebRip),
        (WebDownloadPattern(), ReleaseMedium.WebDownload),
        (HdtvPattern(), ReleaseMedium.Hdtv),
        (DvdRipPattern(), ReleaseMedium.DvdRip),
        (TvRipPattern(), ReleaseMedium.TvRip),
        (CameraPattern(), ReleaseMedium.Camera),
        (DvdPattern(), ReleaseMedium.Dvd),
        (BluRayPattern(), ReleaseMedium.BluRay),
    ];

    /// <summary>The video codecs, under the names the media inspector gives them (MAM-9).</summary>
    private static readonly (Regex Pattern, string Codec)[] VideoCodecs =
    [
        (HevcPattern(), "hevc"),
        (H264Pattern(), "h264"),
        (Av1Pattern(), "av1"),
        (Vc1Pattern(), "vc1"),
        (Mpeg4Pattern(), "mpeg4"),
        (Mpeg2Pattern(), "mpeg2video"),
    ];

    private static readonly (Regex Pattern, DynamicRange Range)[] DynamicRanges =
    [
        (SdrPattern(), DynamicRange.Sdr),
        (Hdr10Pattern(), DynamicRange.Hdr10),
        (HlgPattern(), DynamicRange.Hlg),
        (DolbyVisionPattern(), DynamicRange.DolbyVision),
    ];

    /// <summary>
    /// The short codes a title names a language by, as ISO 639-2/B codes — the bibliographic form a
    /// Matroska track is tagged with, so a claim and a verified track compare as equal strings.
    /// </summary>
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ukr"] = "ukr",
        ["eng"] = "eng",
        ["rus"] = "rus",
        ["jpn"] = "jpn",
        ["jap"] = "jpn",
        ["kor"] = "kor",
        ["chi"] = "chi",
        ["chn"] = "chi",
        ["fre"] = "fre",
        ["fra"] = "fre",
        ["ger"] = "ger",
        ["deu"] = "ger",
        ["spa"] = "spa",
        ["esp"] = "spa",
        ["ita"] = "ita",
        ["pol"] = "pol",
        ["cze"] = "cze",
        ["ces"] = "cze",
        ["por"] = "por",
        ["tur"] = "tur",
        ["multi"] = "mul",
    };

    /// <summary>What <paramref name="title"/> claims; unknown where it says nothing.</summary>
    public static ClaimedReleaseQuality Read(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var tail = TailAfterYear(title);
        var (audio, subtitles) = ReadLanguages(tail);
        return new ClaimedReleaseQuality(
            ReadResolution(tail),
            Media.Where(medium => medium.Pattern.IsMatch(tail)).Select(medium => (ReleaseMedium?)medium.Medium).FirstOrDefault(),
            ReadVideoCodec(tail),
            [.. DynamicRanges.Where(range => range.Pattern.IsMatch(tail)).Select(range => range.Range)],
            audio,
            subtitles);
    }

    private static string TailAfterYear(string title) =>
        YearPattern().Match(title) is { Success: true } year ? title[(year.Index + year.Length)..] : title;

    /// <summary>A named class first ("720p-mini" is 720), then a frame size, then 4K or UHD.</summary>
    private static int? ReadResolution(string tail)
    {
        if (ProgressiveOrInterlacedPattern().Match(tail) is { Success: true } named)
            return int.Parse(named.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        if (FrameSizePattern().Match(tail) is { Success: true } frame)
            return int.Parse(frame.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        return UltraHighDefinitionPattern().IsMatch(tail) ? 2160 : null;
    }

    /// <summary>The codec named first, when a title names two.</summary>
    private static string? ReadVideoCodec(string tail) =>
        VideoCodecs
            .Select(codec => (Match: codec.Pattern.Match(tail), codec.Codec))
            .Where(candidate => candidate.Match.Success)
            .OrderBy(candidate => candidate.Match.Index)
            .Select(candidate => candidate.Codec)
            .FirstOrDefault();

    private static (IReadOnlyList<string> Audio, IReadOnlyList<string> Subtitles) ReadLanguages(string tail)
    {
        var audio = new List<string>();
        var subtitles = new List<string>();
        foreach (var part in tail.Split('|'))
        {
            var subtitlesFrom = SubtitlesPattern().Match(part) is { Success: true } marker ? marker.Index : int.MaxValue;
            foreach (Match language in LanguagePattern().Matches(part))
            {
                var code = Languages[language.Groups[1].Value];
                var list = language.Index > subtitlesFrom ? subtitles : audio;
                if (!list.Contains(code, StringComparer.Ordinal))
                    list.Add(code);
            }
        }

        return (audio, subtitles);
    }

    // Every pattern below stands alone between characters that are neither letters nor digits, so "DV"
    // doesn't match inside "DVDRip", nor "ita" inside "Italian". ASCII digits throughout: \d would
    // take any script's.

    /// <summary>The release year in brackets — "(2014)", "[1986]", or a range "(2008-2014)".</summary>
    [GeneratedRegex(@"[\(\[](?:18|19|20)[0-9]{2}(?:\s*[-–]\s*(?:18|19|20)[0-9]{2})?[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(2160|1080|720|576|480|360)[pi](?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProgressiveOrInterlacedPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[0-9]{3,4}x([0-9]{3,4})(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameSizePattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:4k|uhd)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UltraHighDefinitionPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:bd|uhd)?remux(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemuxPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:bd|br|blu-?ray)-?rip(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluRayRipPattern();

    /// <summary>WEBRip, and WEB-DLRip: a download encoded again is a rip of it.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])web-?(?:dl-?)?rip(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebRipPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])web(?:-?dl)?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebDownloadPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])hdtv(?:-?rip)?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HdtvPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])dvd-?rip(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DvdRipPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:tv|sat|dvb)-?rip(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TvRipPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:cam-?rip|cam|telesync|hd-?ts|ts-?rip)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CameraPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])dvd(?:5|9)?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DvdPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:blu-?ray|bd(?:25|50|66|100))(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BluRayPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:h\.?265|hevc|x265)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HevcPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:h\.?264|avc|x264)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex H264Pattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])av1(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Av1Pattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])vc-?1(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Vc1Pattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:xvid|divx)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Mpeg4Pattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])mpeg-?2(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Mpeg2Pattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])sdr(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SdrPattern();

    /// <summary>HDR, HDR10 and HDR10+ — the PQ formats, as <see cref="DynamicRange.Hdr10"/> covers both.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])hdr(?:10(?:\+|plus)?)?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hdr10Pattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])hlg(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HlgPattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:dv|dovi|dolby[ .-]?vision)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DolbyVisionPattern();

    /// <summary>"Sub", "Subs", "Subtitle(s)": what follows names subtitle languages.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])sub(?:s|titles?)?(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubtitlesPattern();

    /// <summary>A language code, after an optional count of tracks ("2xUkr").</summary>
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?:[0-9]x)?(ukr|eng|rus|jpn|jap|kor|chi|chn|fre|fra|ger|deu|spa|esp|ita|pol|cze|ces|por|tur|multi)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();
}
