using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Paths;

public sealed class PathMappingRulesTests
{
    // The real types' descriptors (F.1): the rules only read the display name and ReportsFilePaths.
    private static readonly IntegrationAdapterDescriptor QBittorrent = new(
        "qbittorrent", "qBittorrent", IntegrationKind.DownloadClient,
        CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.NotUsed, ReportsFilePaths: true);

    private static readonly IntegrationAdapterDescriptor Torznab = new(
        "torznab", "Torznab", IntegrationKind.ReleaseSearch,
        CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false);

    // Spelled out rather than read from PathMappingRules: they are what the dialog shows.
    private const string TooManyError = "An integration can have at most 20 path mappings.";
    private const string ServicePathMissingError = "Each path mapping needs the folder as qBittorrent sees it.";
    private const string AppPathMissingError = "Each path mapping needs the folder as this app sees it.";
    private const string TooLongError = "A path in a path mapping can be at most 1024 characters long.";
    private const string ControlCharacterError = "A path in a path mapping can't contain line breaks or other control characters.";
    private const string StylesError =
        "All the paths of qBittorrent in one integration must be of one kind: Linux-style (/…) or Windows-style (D:\\…).";

    [Fact]
    public void Validate_with_pairs_for_a_type_that_reports_no_paths_is_the_rule()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", "/media")];

        //Act
        var error = PathMappingRules.Validate(pairs, Torznab);

        //Assert
        Assert.Equal("Torznab reports no file paths, so it takes no path mappings.", TestTexts.En(error));
    }

    [Fact]
    public void Validate_with_more_than_20_pairs_is_the_rule()
    {
        //Arrange
        var pairs = Enumerable.Range(1, 21).Select(index => new PathMappingPair($"/downloads/{index}", $"/media/{index}")).ToList();

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(TooManyError, TestTexts.En(error));
    }

    [Fact]
    public void Validate_accepts_20_pairs()
    {
        //Arrange
        var pairs = Enumerable.Range(1, 20).Select(index => new PathMappingPair($"/downloads/{index}", $"/media/{index}")).ToList();

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_with_a_blank_service_path_is_the_rule(string? servicePrefix)
    {
        //Arrange
        PathMappingPair[] pairs = [new(servicePrefix!, "/media")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(ServicePathMissingError, TestTexts.En(error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\t")]
    [InlineData(null)]
    public void Validate_with_a_blank_app_path_is_the_rule(string? appPrefix)
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", appPrefix!)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(AppPathMissingError, TestTexts.En(error));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_with_a_path_longer_than_1024_characters_is_the_rule(bool onTheServiceSide)
    {
        //Arrange
        var tooLong = "/" + new string('a', 1024);
        PathMappingPair[] pairs = [onTheServiceSide ? new(tooLong, "/media") : new("/downloads", tooLong)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(TooLongError, TestTexts.En(error));
    }

    [Fact]
    public void Validate_accepts_paths_of_1024_characters()
    {
        //Arrange
        var longest = "/" + new string('a', 1023);
        PathMappingPair[] pairs = [new(longest, longest)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData("/down\nloads", "/media")]
    [InlineData("/downloads", "/me\rdia")]
    [InlineData("/downloads", "/media/\u0007")]
    public void Validate_with_a_control_character_is_the_rule(string servicePrefix, string appPrefix)
    {
        //Arrange
        PathMappingPair[] pairs = [new(servicePrefix, appPrefix)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(ControlCharacterError, TestTexts.En(error));
    }

    [Theory]
    [InlineData("downloads", "/media", "downloads")]
    [InlineData("~/downloads", "/media", "~/downloads")]
    [InlineData("C:downloads", "/media", "C:downloads")]
    [InlineData("  downloads  ", "/media", "downloads")]
    [InlineData("/downloads", "media", "media")]
    public void Validate_with_a_path_that_is_not_absolute_is_the_rule(string servicePrefix, string appPrefix, string echoed)
    {
        //Arrange
        PathMappingPair[] pairs = [new(servicePrefix, appPrefix)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal($"“{echoed}” is not a full path. Start it with / (Linux) or a drive letter such as D:\\ (Windows).", TestTexts.En(error));
    }

    [Theory]
    [InlineData("/downloads/../etc", "/media", "/downloads/../etc")]
    [InlineData("/downloads", "/media/./movies", "/media/./movies")]
    [InlineData(@"D:\Torrents\..", "/media", @"D:\Torrents\..")]
    public void Validate_with_a_dot_segment_is_the_rule(string servicePrefix, string appPrefix, string echoed)
    {
        //Arrange
        PathMappingPair[] pairs = [new(servicePrefix, appPrefix)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal($"“{echoed}” contains . or .. as a folder name. Write the folder's full path without them.", TestTexts.En(error));
    }

    [Fact]
    public void Validate_with_service_paths_of_two_styles_is_the_rule()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", "/media"), new(@"D:\Torrents", "/media/torrents")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(StylesError, TestTexts.En(error));
    }

    [Theory]
    [InlineData("/downloads", "/downloads//", "/downloads")]
    [InlineData(@"D:\DL", "d:/dl/", @"D:\DL")]
    [InlineData(@"\\NAS\Share\x", @"\\nas\share\X", @"\\NAS\Share\x")]
    public void Validate_with_a_service_path_mapped_twice_is_the_rule(string first, string second, string echoed)
    {
        //Arrange
        PathMappingPair[] pairs = [new(first, "/media"), new(second, "/elsewhere")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal($"The path “{echoed}” of qBittorrent is mapped twice. Keep one mapping for it.", TestTexts.En(error));
    }

    [Fact]
    public void Validate_with_nested_pairs_that_disagree_is_the_rule()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads/movies/", "/films"), new("/downloads", "/media")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(
            "The mappings for “/downloads” and “/downloads/movies” send “/downloads/movies” to different places: " +
            "“/media/movies” and “/films”. Remove one, or make them agree.",
            TestTexts.En(error));
    }

    [Theory]
    // They agree as typed, even when typed in one letter case: the service may report another, and
    // then they don't (PathMapperTests.Map_with_nested_windows_service_pairs_onto_posix_paths_is_ambiguous_in_another_letter_case).
    [InlineData(@"d:\torrents\movies", "/media/movies", @"D:\torrents\movies")]
    [InlineData(@"D:\Torrents\Movies", "/media/Movies", @"D:\Torrents\Movies")]
    public void Validate_with_nested_windows_service_paths_mapped_to_posix_app_paths_is_the_rule(
        string innerService, string innerApp, string echoed)
    {
        //Arrange
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media"), new(innerService, innerApp)];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal(
            $"“{echoed}” is inside “D:\\Torrents”. With Windows paths on the qBittorrent side and Linux paths in this app, keep only one of them.",
            TestTexts.En(error));
    }

    [Fact]
    public void Validate_reports_the_first_broken_pair_before_the_rules_between_pairs()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", "/media"), new("/downloads", "/media"), new("/x", "relative")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Equal("“relative” is not a full path. Start it with / (Linux) or a drive letter such as D:\\ (Windows).", TestTexts.En(error));
    }

    [Fact]
    public void Validate_accepts_no_pairs()
    {
        //Arrange
        PathMappingPair[] none = [];

        //Act
        var forADownloadClient = PathMappingRules.Validate(none, QBittorrent);
        var forASearchSource = PathMappingRules.Validate(none, Torznab);

        //Assert
        Assert.Null(forADownloadClient);
        Assert.Null(forASearchSource);
    }

    [Fact]
    public void Validate_accepts_nested_pairs_that_agree()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", "/media"), new("/downloads/movies", "/media/movies")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Null(error);
    }

    [Fact]
    public void Validate_accepts_two_service_paths_mapped_to_one_app_path()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", "/media"), new("/data", "/media")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Null(error);
    }

    [Fact]
    public void Validate_accepts_posix_paths_that_differ_only_in_case()
    {
        //Arrange
        PathMappingPair[] pairs = [new("/downloads", "/media"), new("/Downloads", "/media/other")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Null(error);
    }

    [Fact]
    public void Validate_accepts_a_windows_service_path_mapped_to_a_posix_app_path()
    {
        //Arrange
        PathMappingPair[] pairs = [new(@"D:\Torrents", "/media"), new(@"\\nas\media", "/media")];

        //Act
        var error = PathMappingRules.Validate(pairs, QBittorrent);

        //Assert
        Assert.Null(error);
    }

    [Fact]
    public void Normalize_trims_writes_the_canonical_form_and_keeps_the_order()
    {
        //Arrange
        PathMappingPair[] pairs =
        [
            new("  /downloads/  ", " /media// "),
            new("d:/DL/", "/media/dl"),
            new(@"\\nas\share\", "/media/nas"),
        ];

        //Act
        var normalized = PathMappingRules.Normalize(pairs);

        //Assert
        Assert.Equal(
            new[]
            {
                new PathMappingPair("/downloads", "/media"),
                new PathMappingPair(@"D:\DL", "/media/dl"),
                new PathMappingPair(@"\\nas\share", "/media/nas"),
            },
            normalized);
    }
}
