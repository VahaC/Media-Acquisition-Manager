using System.Text.Json.Nodes;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// The real ffprobe answers of <c>VendorSamples/Ffprobe</c> (MAM-6) as text and as editable JSON, and
/// variants synthesized from them for what the two files don't show (MAM-9): HDR transfers, Dolby Vision,
/// odd language tags, missing values.
/// </summary>
/// <remarks>
/// Every call returns fresh nodes, parsed anew, so a test can change what it got without touching another
/// test's copy — the <see cref="Integrations.JellyfinSamples"/> pattern.
/// </remarks>
public static class FfprobeSamples
{
    /// <summary>"The Continental" S01E01: HEVC Main 10 without a transfer characteristic, four audio and four
    /// subtitle tracks, then cover art.</summary>
    public const string ContinentalFileName = "the-continental-s01e01.json";

    /// <summary>"Taken" (2008) remux: H.264 High in BT.709, DTS-HD MA and AC-3 tracks, text and PGS subtitles,
    /// then cover art.</summary>
    public const string TakenFileName = "taken-2008-bdremux.json";

    /// <summary>The index of the Continental's first audio stream (Ukrainian AC-3 5.1).</summary>
    public const int ContinentalFirstAudioIndex = 1;

    private static readonly Lazy<string> ContinentalTextValue = new(() => File.ReadAllText(PathOf(ContinentalFileName)));
    private static readonly Lazy<string> TakenTextValue = new(() => File.ReadAllText(PathOf(TakenFileName)));

    /// <summary>Where the Continental's answer is on disk, for a stub ffprobe that prints it.</summary>
    public static string ContinentalPath => PathOf(ContinentalFileName);

    /// <summary>The Continental's answer exactly as ffprobe wrote it.</summary>
    public static string ContinentalText() => ContinentalTextValue.Value;

    /// <summary>Taken's answer exactly as ffprobe wrote it.</summary>
    public static string TakenText() => TakenTextValue.Value;

    /// <summary>The Continental's answer.</summary>
    public static JsonObject Continental() => JsonNode.Parse(ContinentalText())!.AsObject();

    /// <summary>Taken's answer.</summary>
    public static JsonObject Taken() => JsonNode.Parse(TakenText())!.AsObject();

    /// <summary>Stream <paramref name="index"/> of <paramref name="sample"/>, to change in place.</summary>
    public static JsonObject Stream(JsonObject sample, int index) => sample["streams"]![index]!.AsObject();

    /// <summary>The Continental with its video stream's <c>color_transfer</c> set to <paramref name="colorTransfer"/>,
    /// or without one for <c>null</c> (as in the real answer).</summary>
    public static JsonObject ContinentalWithColorTransfer(string? colorTransfer)
    {
        var sample = Continental();
        var video = Stream(sample, 0);
        video.Remove("color_transfer");
        if (colorTransfer is not null)
            video["color_transfer"] = colorTransfer;
        return sample;
    }

    /// <summary>
    /// The Continental as a Dolby Vision profile 8.1 stream: a <c>DOVI configuration record</c> in its side data,
    /// as ffprobe lists it, over an HDR10 base layer (<c>smpte2084</c>).
    /// </summary>
    public static JsonObject ContinentalWithDolbyVision()
    {
        var sample = ContinentalWithColorTransfer("smpte2084");
        Stream(sample, 0)["side_data_list"] = new JsonArray(new JsonObject
        {
            ["side_data_type"] = "DOVI configuration record",
            ["dv_version_major"] = 1,
            ["dv_version_minor"] = 0,
            ["dv_profile"] = 8,
            ["dv_level"] = 6,
            ["rpu_present_flag"] = 1,
            ["el_present_flag"] = 0,
            ["bl_present_flag"] = 1,
            ["dv_bl_signal_compatibility_id"] = 1,
        });
        return sample;
    }

    /// <summary>The Continental with its first audio track tagged <paramref name="language"/>, or untagged for
    /// <c>null</c>.</summary>
    public static JsonObject ContinentalWithFirstAudioLanguage(string? language)
    {
        var sample = Continental();
        var tags = Stream(sample, ContinentalFirstAudioIndex)["tags"]!.AsObject();
        tags.Remove("language");
        if (language is not null)
            tags["language"] = language;
        return sample;
    }

    /// <summary>The Continental with its <c>format</c> section's <paramref name="property"/> set to
    /// <paramref name="value"/>, or removed for <c>null</c>.</summary>
    public static JsonObject ContinentalWithFormat(string property, JsonNode? value)
    {
        var sample = Continental();
        var format = sample["format"]!.AsObject();
        format.Remove(property);
        if (value is not null)
            format[property] = value;
        return sample;
    }

    private static string PathOf(string fileName) => Path.Combine(RepositoryPaths.VendorSamples, "Ffprobe", fileName);
}
