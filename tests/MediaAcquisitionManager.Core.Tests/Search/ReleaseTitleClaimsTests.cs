using System.Xml.Linq;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-10 — what a release's title claims (specification §3.1), read against every distinct title the
// real Toloka search answered (VendorSamples/Torznab, 17 releases, 15 titles) and the shapes other
// trackers write.
public sealed class ReleaseTitleClaimsTests
{
    private static readonly DynamicRange[] NoRange = [];
    private static readonly string[] None = [];
    private static readonly string[] UkrainianAndEnglish = ["ukr", "eng"];
    private static readonly string[] English = ["eng"];

    /// <summary>Each distinct title of the real search, with everything it claims.</summary>
    public static TheoryData<string, int?, ReleaseMedium?, string?, DynamicRange[], string[], string[]> SampleTitles() => new()
    {
        { "Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", 1080, ReleaseMedium.BluRayRemux, null, NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) BDRip 1080p Ukr/Eng | Sub Eng [IMAX Edition]", 1080, ReleaseMedium.BluRayRip, null, NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) BDRip 4K 2160p H.265 HDR [IMAX] Ukr/Eng | Sub Eng", 2160, ReleaseMedium.BluRayRip, "hevc", [DynamicRange.Hdr10], UkrainianAndEnglish, English },
        { "Interstellar (2014) [DTS, AC3]", null, null, null, NoRange, None, None },
        {
            "Interstellar (2014) BDRemux 4K 2160p HDR Dolby Vision H.265 [Hybrid] [IMAX Edition] Ukr/Eng | Sub Eng",
            2160, ReleaseMedium.BluRayRemux, "hevc", [DynamicRange.Hdr10, DynamicRange.DolbyVision], UkrainianAndEnglish, English
        },
        { "Interstellar (2014) [IMAX] BDRip 1080p H.265 Ukr/Eng | Sub Eng", 1080, ReleaseMedium.BluRayRip, "hevc", NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) UHD BDRemux 4K 2160p [IMAX] Ukr/Eng | Sub Eng", 2160, ReleaseMedium.BluRayRemux, null, NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) BDRip [640x360]", 360, ReleaseMedium.BluRayRip, null, NoRange, None, None },
        { "Interstellar (2014) BDRip [IMAX Edition] Ukr/Eng | Sub Eng", null, ReleaseMedium.BluRayRip, null, NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) BDRip 720p-mini [60fps] Ukr/Eng | Sub Eng [IMAX Edition]", 720, ReleaseMedium.BluRayRip, null, NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) [2-Disc IMAX Edition] Blu-ray CEE Multi | Sub Multi", null, ReleaseMedium.BluRay, null, NoRange, ["mul"], ["mul"] },
        { "Interstellar (2014) BDRip 720p-mini | IMAX", 720, ReleaseMedium.BluRayRip, null, NoRange, None, None },
        { "Interstellar (2014) BDRip-AVC", null, ReleaseMedium.BluRayRip, "h264", NoRange, None, None },
        { "Interstellar (2014) BDRip 720p Ukr/Eng | Sub Eng [IMAX Edition]", 720, ReleaseMedium.BluRayRip, null, NoRange, UkrainianAndEnglish, English },
        { "Interstellar (2014) BDRip", null, ReleaseMedium.BluRayRip, null, NoRange, None, None },
    };

    [Theory]
    [MemberData(nameof(SampleTitles))]
    public void Read_of_each_real_toloka_title_claims_what_it_says(
        string title,
        int? resolution,
        ReleaseMedium? medium,
        string? videoCodec,
        DynamicRange[] dynamicRanges,
        string[] audioLanguages,
        string[] subtitleLanguages)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(resolution, claims.Resolution);
        Assert.Equal(medium, claims.Medium);
        Assert.Equal(videoCodec, claims.VideoCodec);
        Assert.Equal(dynamicRanges, claims.DynamicRanges);
        Assert.Equal(audioLanguages, claims.AudioLanguages);
        Assert.Equal(subtitleLanguages, claims.SubtitleLanguages);
    }

    [Theory]
    [InlineData("prowlarr-toloka-search.xml")]
    [InlineData("prowlarr-toloka-movie-search.xml")]
    public void SampleTitles_cover_every_title_of_the_real_answers(string sample)
    {
        //Arrange
        var document = XDocument.Load(Path.Combine(RepositoryPaths.VendorSamples, "Torznab", sample));

        //Act
        var titles = document.Descendants("item").Select(item => (string)item.Element("title")!).ToHashSet(StringComparer.Ordinal);

        //Assert
        // Guards the table above: a refreshed sample with a new title fails here until the title is read.
        Assert.Equal(15, titles.Count);
        Assert.Equal(titles, SampleTitles().Select(row => row.Data.Item1).ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Movie (2020) 2160p", 2160)]
    [InlineData("Movie (2020) 4K", 2160)]
    [InlineData("Movie (2020) UHD", 2160)]
    [InlineData("Movie (2020) 1080i HDTV", 1080)]
    [InlineData("Movie (2020) 576p", 576)]
    [InlineData("Movie (2020) 480p DVDRip", 480)]
    [InlineData("Movie.2020.1080p.WEB-DL", 1080)]
    [InlineData("Movie (2020) [1920x800]", 800)]
    // The named class decides over the frame size and 4K.
    [InlineData("Movie (2020) 4K 1080p [3840x2160]", 1080)]
    [InlineData("Movie (2020) 1080 BDRip", null)]
    [InlineData("Movie (2020) 10800p", null)]
    public void Read_takes_the_resolution_class_the_title_names(string title, int? expected)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(expected, claims.Resolution);
    }

    [Theory]
    [InlineData("Movie (2020) BDRemux", ReleaseMedium.BluRayRemux)]
    [InlineData("Movie (2020) UHD BDRemux", ReleaseMedium.BluRayRemux)]
    [InlineData("Movie.2020.1080p.BluRay.REMUX.AVC", ReleaseMedium.BluRayRemux)]
    [InlineData("Movie (2020) Blu-ray CEE", ReleaseMedium.BluRay)]
    [InlineData("Movie (2020) BD50", ReleaseMedium.BluRay)]
    [InlineData("Movie (2020) BDRip", ReleaseMedium.BluRayRip)]
    [InlineData("Movie (2020) BRRip", ReleaseMedium.BluRayRip)]
    [InlineData("Movie (2020) WEB-DL 1080p", ReleaseMedium.WebDownload)]
    [InlineData("Movie.2020.1080p.WEB.H264", ReleaseMedium.WebDownload)]
    [InlineData("Movie (2020) WEBRip", ReleaseMedium.WebRip)]
    [InlineData("Movie (2020) WEB-DLRip", ReleaseMedium.WebRip)]
    [InlineData("Movie (2020) HDTVRip", ReleaseMedium.Hdtv)]
    [InlineData("Movie (2020) HDTV 1080i", ReleaseMedium.Hdtv)]
    [InlineData("Movie (2020) DVD9", ReleaseMedium.Dvd)]
    [InlineData("Movie (2020) DVD", ReleaseMedium.Dvd)]
    [InlineData("Movie (2020) DVDRip", ReleaseMedium.DvdRip)]
    [InlineData("Movie (2020) SATRip", ReleaseMedium.TvRip)]
    [InlineData("Movie (2020) TVRip", ReleaseMedium.TvRip)]
    [InlineData("Movie (2020) CAMRip", ReleaseMedium.Camera)]
    [InlineData("Movie (2020) TeleSync", ReleaseMedium.Camera)]
    [InlineData("Movie (2020) HDRip", null)]
    [InlineData("Movie (2020)", null)]
    public void Read_names_what_the_release_was_made_from(string title, ReleaseMedium? expected)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(expected, claims.Medium);
    }

    [Theory]
    [InlineData("Movie (2020) H.265", "hevc")]
    [InlineData("Movie (2020) HEVC", "hevc")]
    [InlineData("Movie.2020.2160p.x265", "hevc")]
    [InlineData("Movie (2020) H.264", "h264")]
    [InlineData("Movie (2020) BDRip-AVC", "h264")]
    [InlineData("Movie.2020.1080p.x264", "h264")]
    [InlineData("Movie (2020) AV1", "av1")]
    [InlineData("Movie (2020) VC-1", "vc1")]
    [InlineData("Movie (2020) DVDRip XviD", "mpeg4")]
    [InlineData("Movie (2020) DVD9 MPEG-2", "mpeg2video")]
    // Two named: the one named first.
    [InlineData("Movie (2020) x265 / x264", "hevc")]
    [InlineData("Movie (2020) BDRip", null)]
    public void Read_names_the_video_codec_as_the_media_inspector_does(string title, string? expected)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(expected, claims.VideoCodec);
    }

    [Theory]
    [InlineData("Movie (2020) HDR", new[] { DynamicRange.Hdr10 })]
    [InlineData("Movie (2020) HDR10", new[] { DynamicRange.Hdr10 })]
    [InlineData("Movie (2020) HDR10+", new[] { DynamicRange.Hdr10 })]
    [InlineData("Movie (2020) DV HDR10", new[] { DynamicRange.Hdr10, DynamicRange.DolbyVision })]
    [InlineData("Movie (2020) DoVi", new[] { DynamicRange.DolbyVision })]
    [InlineData("Movie (2020) HLG", new[] { DynamicRange.Hlg })]
    [InlineData("Movie (2020) SDR", new[] { DynamicRange.Sdr })]
    // Not said is unknown, never SDR; and HDRip or DVDRip are no dynamic range.
    [InlineData("Movie (2020) 2160p", new DynamicRange[0])]
    [InlineData("Movie (2020) HDRip DVDRip", new DynamicRange[0])]
    public void Read_lists_every_dynamic_range_the_title_names(string title, DynamicRange[] expected)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(expected, claims.DynamicRanges);
    }

    [Theory]
    [InlineData("Movie (2020) 2xUkr/Eng | Sub Ukr/Eng", new[] { "ukr", "eng" }, new[] { "ukr", "eng" })]
    [InlineData("Movie (2020) Ukr/Eng/Jpn | Subs Ukr", new[] { "ukr", "eng", "jpn" }, new[] { "ukr" })]
    [InlineData("Movie (2020) UKR ENG Sub ENG", new[] { "ukr", "eng" }, new[] { "eng" })]
    [InlineData("Movie (2020) Fra/Deu/Esp/Ita/Pol", new[] { "fre", "ger", "spa", "ita", "pol" }, new string[0])]
    [InlineData("Movie (2020) Ukr | Ukr", new[] { "ukr" }, new string[0])]
    [InlineData("Movie (2020) BDRip", new string[0], new string[0])]
    public void Read_lists_audio_languages_and_subtitle_languages_after_sub(string title, string[] audio, string[] subtitles)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(audio, claims.AudioLanguages);
        Assert.Equal(subtitles, claims.SubtitleLanguages);
    }

    [Theory]
    // The film's own name is not read once the year is found.
    [InlineData("The English Patient (1996) BDRip", new string[0])]
    [InlineData("Eng Ukr (2020) BDRip Ukr", new[] { "ukr" })]
    [InlineData("Taken Trilogy (2008-2014) BDRemux Eng", new[] { "eng" })]
    [InlineData("Movie [2020] Pol", new[] { "pol" })]
    // Without a year in brackets, the whole title is read.
    [InlineData("Movie.Ukr.1080p", new[] { "ukr" })]
    public void Read_reads_only_what_follows_the_year(string title, string[] audio)
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read(title);

        //Assert
        Assert.Equal(audio, claims.AudioLanguages);
    }

    [Fact]
    public void Read_reads_no_resolution_from_a_film_named_after_one()
    {
        //Arrange

        //Act
        var claims = ReleaseTitleClaims.Read("4K 1080p (2021) DVDRip");

        //Assert
        Assert.Null(claims.Resolution);
        Assert.Equal(ReleaseMedium.DvdRip, claims.Medium);
    }
}
