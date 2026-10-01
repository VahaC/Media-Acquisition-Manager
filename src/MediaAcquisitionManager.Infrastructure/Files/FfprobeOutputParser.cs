using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Reads ffprobe's JSON (<c>-show_format -show_streams</c>) into <see cref="MediaFacts"/> (MAM-9): an
/// allowlist, every value read by its property name, nothing passed through. A value that is missing, of
/// the wrong type or not what ffprobe writes stays <c>null</c>, never a guess (specification §3.1).
/// </summary>
/// <remarks>
/// Tested against real answers in <c>VendorSamples/Ffprobe</c> and variants built from them
/// (<c>FfprobeSamples</c>): what the samples taught is that cover art comes as a video stream marked
/// <c>attached_pic</c>, after the real one.
/// </remarks>
internal static partial class FfprobeOutputParser
{
    /// <summary>The side data ffprobe lists for a Dolby Vision stream; its transfer says only the base layer's
    /// HDR10 or SDR.</summary>
    private const string DolbyVisionSideData = "DOVI configuration record";

    private const string UndeterminedLanguage = "und";

    /// <summary>
    /// The JSON as ffprobe wrote it: <c>true</c> with what it says, or <c>false</c> when it is not ffprobe's
    /// answer — not JSON, not an object, or without the <c>format</c> section <c>-show_format</c> always
    /// writes. An empty object read as a file with no tracks would be a guess, and it would be cached.
    /// </summary>
    public static bool TryParse(string output, [NotNullWhen(true)] out MediaFacts? facts)
    {
        facts = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var tracks = new List<MediaTrackFacts>();
            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.ValueKind == JsonValueKind.Object && ReadTrack(stream) is { } track)
                        tracks.Add(track);
                }
            }

            facts = new MediaFacts(ReadDuration(format), ReadBitRate(format), tracks.AsReadOnly());
            return true;
        }
    }

    // ── tracks ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A video, audio or subtitle stream; <c>null</c> for cover art and for every other kind (data,
    /// attachments such as fonts).</summary>
    private static MediaTrackFacts? ReadTrack(JsonElement stream)
    {
        MediaTrackKind? kind = Text(stream, "codec_type") switch
        {
            "video" => MediaTrackKind.Video,
            "audio" => MediaTrackKind.Audio,
            "subtitle" => MediaTrackKind.Subtitle,
            _ => null,
        };
        if (kind is null)
            return null;

        var disposition = stream.TryGetProperty("disposition", out var found) ? found : default;
        if (kind == MediaTrackKind.Video && IsSet(disposition, "attached_pic"))
            return null;

        var isVideo = kind == MediaTrackKind.Video;
        var isAudio = kind == MediaTrackKind.Audio;
        return new MediaTrackFacts(
            kind.Value,
            Name(stream, "codec_name"),
            Name(stream, "profile"),
            // A subtitle's picture size (PGS has one) is not a resolution: only a video's counts.
            isVideo ? Positive(stream, "width") : null,
            isVideo ? Positive(stream, "height") : null,
            isVideo ? ReadDynamicRange(stream) : null,
            isAudio ? Positive(stream, "channels") : null,
            isAudio ? Name(stream, "channel_layout") : null,
            ReadLanguage(stream),
            IsSet(disposition, "default"),
            IsSet(disposition, "forced"),
            IsSet(disposition, "hearing_impaired"));
    }

    /// <summary>
    /// Dolby Vision by its configuration record; otherwise by the transfer characteristic. Without one, or
    /// with one that says nothing (<c>unknown</c>, <c>unspecified</c>, <c>reserved</c>), it is unknown: a 10-bit
    /// HEVC stream without a transfer (The Continental's) can be HDR or not, and calling it SDR would be a
    /// guess.
    /// </summary>
    private static DynamicRange? ReadDynamicRange(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sideData)
            && sideData.ValueKind == JsonValueKind.Array
            && sideData.EnumerateArray().Any(entry => Text(entry, "side_data_type") == DolbyVisionSideData))
        {
            return DynamicRange.DolbyVision;
        }

        return Text(stream, "color_transfer")?.Trim() switch
        {
            null or "" or "unknown" or "unspecified" or "reserved" => null,
            "smpte2084" => DynamicRange.Hdr10,
            "arib-std-b67" => DynamicRange.Hlg,
            // bt709, bt470bg, smpte170m, bt2020-10 and the rest: a standard-range transfer.
            _ => DynamicRange.Sdr,
        };
    }

    /// <summary>
    /// The language tag, lower case: an ISO 639 code or a BCP 47 tag built on one (<c>ukr</c>, <c>en-us</c>).
    /// <c>und</c> means undetermined and anything else is not a language, so both are unknown — a junk tag
    /// stored would be shown as a language.
    /// </summary>
    private static string? ReadLanguage(JsonElement stream)
    {
        if (!stream.TryGetProperty("tags", out var tags) || Text(tags, "language") is not { } tag)
            return null;

        var language = tag.ToLowerInvariant();
        return language != UndeterminedLanguage
            && language.Length <= MediaInspectionTrackEntity.LanguageMaximumLength
            && LanguageTag().IsMatch(language)
                ? language
                : null;
    }

    // ── format ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seconds as ffprobe writes them (<c>"5179.168000"</c>, invariant); <c>N/A</c>, zero, negative or
    /// out of range is unknown.</summary>
    private static TimeSpan? ReadDuration(JsonElement format)
    {
        if (Text(format, "duration") is not { } text
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        // Written so NaN fails too.
        if (!(seconds > 0) || seconds >= TimeSpan.MaxValue.TotalSeconds)
            return null;

        // By ticks rather than FromSeconds(double), so the milliseconds are exactly the ones written.
        return TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    }

    /// <summary>Bits per second as ffprobe writes them (<c>"6011343"</c>); <c>N/A</c> or zero is unknown.</summary>
    private static long? ReadBitRate(JsonElement format) =>
        Text(format, "bit_rate") is { } text
        && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var bitRate)
        && bitRate > 0
            ? bitRate
            : null;

    // ── values ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A string property, or <c>null</c> when it is missing or not a string.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// A name ffprobe gives (a codec, a profile, a channel layout), trimmed. ffprobe's names are short tokens,
    /// so one with control characters or longer than the column (<see cref="MediaInspectionTrackEntity.NameMaximumLength"/>)
    /// is not one of them, and is unknown rather than cut.
    /// </summary>
    private static string? Name(JsonElement element, string name)
    {
        var value = Text(element, name)?.Trim();
        return string.IsNullOrEmpty(value) || value.Length > MediaInspectionTrackEntity.NameMaximumLength || value.Any(char.IsControl)
            ? null
            : value;
    }

    /// <summary>A positive whole number; zero (ffprobe's "not known") and anything else is unknown.</summary>
    private static int? Positive(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
        && number > 0
            ? number
            : null;

    /// <summary>A disposition flag: ffprobe writes 1 for set and 0 for not.</summary>
    private static bool IsSet(JsonElement disposition, string name) =>
        disposition.ValueKind == JsonValueKind.Object
        && disposition.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var flag)
        && flag == 1;

    /// <summary>
    /// An ISO 639-1 or 639-2 code, optionally followed by BCP 47 subtags. <c>\z</c> rather than <c>$</c>,
    /// which would let a trailing line break through.
    /// </summary>
    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z0-9]{1,8})*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTag();
}
