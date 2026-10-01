using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-9 — ffprobe's JSON as the inspection reads it: the two real answers of the reference library
// (VendorSamples/Ffprobe) field by field, and variants synthesized from them (FfprobeSamples) for what they
// don't show. Every value is read by name, and one that is missing or isn't ffprobe's stays unknown.
public sealed class FfprobeOutputParserTests
{
    [Fact]
    public void TryParse_reads_the_continental_duration_and_bit_rate()
    {
        //Arrange
        var output = FfprobeSamples.ContinentalText();

        //Act
        var parsed = FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.True(parsed);
        // "5179.168000" seconds, to the millisecond.
        Assert.Equal(5_179_168, facts!.Duration!.Value.TotalMilliseconds);
        Assert.Equal(6_011_343, facts.BitRate);
    }

    [Fact]
    public void TryParse_reads_every_continental_track_in_file_order_and_leaves_the_cover_art_out()
    {
        //Arrange
        var output = FfprobeSamples.ContinentalText();

        //Act
        FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        // The tenth stream is the mjpeg cover (attached_pic), not a second video track. The HEVC Main 10
        // stream has no color_transfer, so its dynamic range is unknown rather than guessed.
        Assert.Equal(
            [
                Video("hevc", "Main 10", 1920, 800, null, "eng", isDefault: true),
                Audio("ac3", null, 6, "5.1(side)", "ukr", isDefault: true),
                Audio("aac", "LC", 2, "stereo", "ukr"),
                Audio("ac3", null, 2, "stereo", "ukr"),
                Audio("ac3", null, 6, "5.1(side)", "eng"),
                Subtitle("subrip", "ukr", isDefault: true, isForced: true),
                Subtitle("subrip", "ukr"),
                Subtitle("subrip", "eng"),
                Subtitle("subrip", "eng", isHearingImpaired: true),
            ],
            facts!.Tracks);
    }

    [Fact]
    public void TryParse_reads_the_taken_duration_and_bit_rate()
    {
        //Arrange
        var output = FfprobeSamples.TakenText();

        //Act
        var parsed = FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.True(parsed);
        Assert.Equal(5_604_672, facts!.Duration!.Value.TotalMilliseconds);
        Assert.Equal(40_781_887, facts.BitRate);
    }

    [Fact]
    public void TryParse_reads_every_taken_track_with_bt709_as_sdr_and_leaves_the_cover_art_out()
    {
        //Arrange
        var output = FfprobeSamples.TakenText();

        //Act
        FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.Equal(
            [
                Video("h264", "High", 1920, 1080, DynamicRange.Sdr, "ukr", isDefault: true, isForced: true),
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
            ],
            facts!.Tracks);
    }

    [Theory]
    [InlineData("smpte2084", DynamicRange.Hdr10)]
    [InlineData("arib-std-b67", DynamicRange.Hlg)]
    [InlineData("bt709", DynamicRange.Sdr)]
    [InlineData("bt2020-10", DynamicRange.Sdr)]
    [InlineData("smpte170m", DynamicRange.Sdr)]
    [InlineData("unknown", null)]
    [InlineData("unspecified", null)]
    [InlineData("reserved", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TryParse_reads_the_dynamic_range_from_the_transfer_characteristic(string? colorTransfer, DynamicRange? expected)
    {
        //Arrange
        var output = FfprobeSamples.ContinentalWithColorTransfer(colorTransfer).ToJsonString();

        //Act
        FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.Equal(expected, facts!.Tracks[0].DynamicRange);
    }

    [Fact]
    public void TryParse_reads_a_dovi_configuration_record_as_dolby_vision_whatever_the_transfer()
    {
        //Arrange
        // Profile 8.1: an HDR10 base layer (smpte2084) under the Dolby Vision one.
        var output = FfprobeSamples.ContinentalWithDolbyVision().ToJsonString();

        //Act
        FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.Equal(DynamicRange.DolbyVision, facts!.Tracks[0].DynamicRange);
    }

    [Fact]
    public void TryParse_ignores_side_data_of_another_type()
    {
        //Arrange
        var sample = FfprobeSamples.ContinentalWithColorTransfer("bt709");
        FfprobeSamples.Stream(sample, 0)["side_data_list"] = new JsonArray(new JsonObject { ["side_data_type"] = "Display Matrix" });

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.Equal(DynamicRange.Sdr, facts!.Tracks[0].DynamicRange);
    }

    [Theory]
    [InlineData("ukr", "ukr")]
    [InlineData("UKR", "ukr")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en-us")]
    [InlineData("zh-Hant-TW", "zh-hant-tw")]
    [InlineData("und", null)]
    [InlineData("UND", null)]
    [InlineData("", null)]
    [InlineData("english", null)]
    [InlineData("u1", null)]
    [InlineData("uk_UA", null)]
    [InlineData(" ukr", null)]
    [InlineData("ukr\n", null)]
    [InlineData("en-abcdefghi", null)]
    [InlineData(null, null)]
    public void TryParse_reads_a_language_tag_lower_case_and_leaves_anything_else_unknown(string? tag, string? expected)
    {
        //Arrange
        var output = FfprobeSamples.ContinentalWithFirstAudioLanguage(tag).ToJsonString();

        //Act
        FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.Equal(expected, facts!.Tracks[FfprobeSamples.ContinentalFirstAudioIndex].Language);
    }

    [Fact]
    public void TryParse_leaves_a_language_tag_longer_than_the_column_unknown()
    {
        //Arrange
        // 36 characters, each subtag well formed: only the length makes it unusable.
        var tag = "en-" + string.Join('-', Enumerable.Repeat("abcdefgh", 3)) + "-abcdef";
        var output = FfprobeSamples.ContinentalWithFirstAudioLanguage(tag).ToJsonString();

        //Act
        FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.Equal(36, tag.Length);
        Assert.Null(facts!.Tracks[FfprobeSamples.ContinentalFirstAudioIndex].Language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"N/A\"")]
    [InlineData("\"0.000000\"")]
    [InlineData("\"-1.5\"")]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\"1e300\"")]
    [InlineData("\"five\"")]
    [InlineData("5179.168")]
    public void TryParse_leaves_a_missing_or_meaningless_duration_unknown(string? json)
    {
        //Arrange
        var output = FfprobeSamples.ContinentalWithFormat("duration", json is null ? null : JsonNode.Parse(json)).ToJsonString();

        //Act
        var parsed = FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.True(parsed);
        Assert.Null(facts!.Duration);
        Assert.Equal(6_011_343, facts.BitRate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"N/A\"")]
    [InlineData("\"0\"")]
    [InlineData("\"-6011343\"")]
    [InlineData("\"6011343.5\"")]
    [InlineData("\"99999999999999999999\"")]
    [InlineData("6011343")]
    public void TryParse_leaves_a_missing_or_meaningless_bit_rate_unknown(string? json)
    {
        //Arrange
        var output = FfprobeSamples.ContinentalWithFormat("bit_rate", json is null ? null : JsonNode.Parse(json)).ToJsonString();

        //Act
        var parsed = FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.True(parsed);
        Assert.Null(facts!.BitRate);
        Assert.Equal(5_179_168, facts.Duration!.Value.TotalMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\n\n}")]
    [InlineData("[]")]
    [InlineData("\"format\"")]
    [InlineData("{\"streams\": []}")]
    [InlineData("{\"format\": []}")]
    [InlineData("{\"format\": {\"duration\": \"1.0\"")]
    public void TryParse_refuses_what_is_not_ffprobes_answer(string output)
    {
        //Arrange
        // "{\n\n}" is what ffprobe prints when it fails on a file; the exit code says so too, but an empty
        // answer read as a file without tracks would be cached as one.

        //Act
        var parsed = FfprobeOutputParser.TryParse(output, out var facts);

        //Assert
        Assert.False(parsed);
        Assert.Null(facts);
    }

    [Fact]
    public void TryParse_refuses_a_sample_cut_short()
    {
        //Arrange
        var text = FfprobeSamples.TakenText();
        var output = text[..(text.Length / 2)];

        //Act
        var parsed = FfprobeOutputParser.TryParse(output, out _);

        //Assert
        Assert.False(parsed);
    }

    [Fact]
    public void TryParse_reads_a_format_without_streams_as_a_file_without_tracks()
    {
        //Arrange
        var sample = FfprobeSamples.Continental();
        sample.Remove("streams");

        //Act
        var parsed = FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.True(parsed);
        Assert.Empty(facts!.Tracks);
        Assert.Equal(6_011_343, facts.BitRate);
    }

    [Theory]
    [InlineData("hevc\u0007")]
    [InlineData("hevc\nh264")]
    [InlineData("   ")]
    [InlineData("")]
    public void TryParse_leaves_a_codec_name_with_control_characters_or_nothing_in_it_unknown(string codec)
    {
        //Arrange
        var sample = FfprobeSamples.Continental();
        FfprobeSamples.Stream(sample, 0)["codec_name"] = codec;

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.Null(facts!.Tracks[0].Codec);
    }

    [Fact]
    public void TryParse_trims_names_and_leaves_one_longer_than_the_column_unknown()
    {
        //Arrange
        var sample = FfprobeSamples.Continental();
        FfprobeSamples.Stream(sample, 0)["codec_name"] = " hevc ";
        FfprobeSamples.Stream(sample, 0)["profile"] = new string('x', 65);
        FfprobeSamples.Stream(sample, FfprobeSamples.ContinentalFirstAudioIndex)["channel_layout"] = new string('x', 64);

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.Equal("hevc", facts!.Tracks[0].Codec);
        Assert.Null(facts.Tracks[0].Profile);
        Assert.Equal(new string('x', 64), facts.Tracks[FfprobeSamples.ContinentalFirstAudioIndex].ChannelLayout);
    }

    [Fact]
    public void TryParse_reads_sizes_only_for_video_and_channels_only_for_audio()
    {
        //Arrange
        // PGS subtitles carry a picture size, and nothing stops a stream from claiming channels it can't have.
        var sample = FfprobeSamples.Continental();
        var audio = FfprobeSamples.Stream(sample, FfprobeSamples.ContinentalFirstAudioIndex);
        audio["width"] = 1920;
        audio["color_transfer"] = "smpte2084";
        var subtitle = FfprobeSamples.Stream(sample, 5);
        subtitle["width"] = 1920;
        subtitle["height"] = 1080;
        subtitle["channels"] = 2;
        subtitle["channel_layout"] = "stereo";
        var video = FfprobeSamples.Stream(sample, 0);
        video["channels"] = 2;

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        var tracks = facts!.Tracks;
        Assert.Null(tracks[FfprobeSamples.ContinentalFirstAudioIndex].Width);
        Assert.Null(tracks[FfprobeSamples.ContinentalFirstAudioIndex].DynamicRange);
        Assert.Equal((null, null, null, null), (tracks[5].Width, tracks[5].Height, tracks[5].Channels, tracks[5].ChannelLayout));
        Assert.Null(tracks[0].Channels);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1920")]
    [InlineData("\"1920\"")]
    [InlineData("1920.5")]
    [InlineData("99999999999")]
    public void TryParse_leaves_a_size_that_is_not_a_positive_whole_number_unknown(string json)
    {
        //Arrange
        var sample = FfprobeSamples.Continental();
        FfprobeSamples.Stream(sample, 0)["width"] = JsonNode.Parse(json);

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.Null(facts!.Tracks[0].Width);
        Assert.Equal(800, facts.Tracks[0].Height);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("attachment")]
    [InlineData("VIDEO")]
    public void TryParse_leaves_out_streams_of_any_other_kind(string codecType)
    {
        //Arrange
        var sample = FfprobeSamples.Continental();
        FfprobeSamples.Stream(sample, 2)["codec_type"] = codecType;

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.Equal(8, facts!.Tracks.Count);
        Assert.Equal("ac3", facts.Tracks[2].Codec);
    }

    [Fact]
    public void TryParse_counts_a_disposition_flag_only_when_it_is_one()
    {
        //Arrange
        var sample = FfprobeSamples.Continental();
        var disposition = FfprobeSamples.Stream(sample, 6)["disposition"]!.AsObject();
        disposition["default"] = 2;
        disposition["forced"] = "1";
        disposition["hearing_impaired"] = true;

        //Act
        FfprobeOutputParser.TryParse(sample.ToJsonString(), out var facts);

        //Assert
        Assert.Equal((false, false, false), (facts!.Tracks[6].IsDefault, facts.Tracks[6].IsForced, facts.Tracks[6].IsHearingImpaired));
    }

    private static MediaTrackFacts Video(
        string codec, string profile, int width, int height, DynamicRange? dynamicRange, string language,
        bool isDefault = false, bool isForced = false) =>
        new(MediaTrackKind.Video, codec, profile, width, height, dynamicRange, null, null, language, isDefault, isForced, false);

    private static MediaTrackFacts Audio(
        string codec, string? profile, int channels, string channelLayout, string language, bool isDefault = false, bool isForced = false) =>
        new(MediaTrackKind.Audio, codec, profile, null, null, null, channels, channelLayout, language, isDefault, isForced, false);

    private static MediaTrackFacts Subtitle(
        string codec, string language, bool isDefault = false, bool isForced = false, bool isHearingImpaired = false) =>
        new(MediaTrackKind.Subtitle, codec, null, null, null, null, null, null, language, isDefault, isForced, isHearingImpaired);
}
