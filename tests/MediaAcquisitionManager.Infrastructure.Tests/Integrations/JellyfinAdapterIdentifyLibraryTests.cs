using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Integrations.JellyfinIdentifyHarness;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — files and folders are never touched (decision 5 of ADR 0026): before any search the options of the library
// that holds the item are read, and an Identify is refused in one that saves beside the media — artwork and metadata, NFO files,
// trickplay images, downloaded subtitles — or whose options can't be read: the app fails closed. Which library holds an item is
// told by its path against the libraries' locations (the item carries no library id). The libraries are the five of the
// recorded virtual-folders.json: four that save nothing beside the media and "Колекції", which does.
public sealed class JellyfinAdapterIdentifyLibraryTests : IDisposable
{
    private const string CartoonsLibrary = "Мульфільми";

    private readonly JellyfinIdentifyHarness _jellyfin = new();

    public void Dispose() => _jellyfin.Dispose();

    [Theory]
    [InlineData("SaveLocalMetadata", "artwork and metadata")]
    [InlineData("MetadataSavers", "NFO files")]
    [InlineData("MetadataSaversLowerCase", "NFO files")]
    [InlineData("SaveTrickplayWithMedia", "trickplay images")]
    [InlineData("SubtitleDownloads", "downloaded subtitles")]
    [InlineData("SubtitleDownloadsWithoutTheSetting", "downloaded subtitles")]
    public async Task Identify_in_a_library_that_saves_beside_the_media_is_refused_before_any_search_or_apply(string option, string expectedSaves)
    {
        //Arrange
        var options = _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot);
        switch (option)
        {
            case "SaveLocalMetadata":
                options["SaveLocalMetadata"] = true;
                break;
            case "MetadataSavers":
                options["MetadataSavers"] = new JsonArray("Nfo");
                break;
            case "MetadataSaversLowerCase":
                options["MetadataSavers"] = new JsonArray("nfo");
                break;
            case "SaveTrickplayWithMedia":
                options["SaveTrickplayWithMedia"] = true;
                break;
            case "SubtitleDownloads":
                options["SubtitleDownloadLanguages"] = new JsonArray("eng");
                options["SaveSubtitlesWithMedia"] = true;
                break;
            default:
                options["SubtitleDownloadLanguages"] = new JsonArray("eng");
                options.Remove("SaveSubtitlesWithMedia");
                break;
        }

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Refused, identification.Outcome);
        Assert.Equal(LibraryIdentifyRefusal.LibraryWritesFiles, identification.Refusal);
        Assert.False(identification.Sent);
        Assert.Equal(
            $"Jellyfin's library “{CartoonsLibrary}” saves {expectedSaves} next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
            TestTexts.En(identification.Sentence));
        // The users, the item and the libraries, and nothing after: no search, no Apply.
        Assert.Equal(3, _jellyfin.Http.Requests.Count);
        Assert.Equal("/Library/VirtualFolders", _jellyfin.Http.Requests[^1].Uri.AbsolutePath);
        Assert.Empty(_jellyfin.Server.RemoteSearches);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_in_the_box_set_library_the_reference_installation_has_is_refused_naming_what_it_saves()
    {
        //Arrange
        // "Колекції": SaveLocalMetadata true and no savers list, as recorded in virtual-folders.json.
        _jellyfin.AddFilmUnder(JellyfinSamples.CollectionsRoot, "44444444444444444444444444444444");
        var request = FilmRequest() with { SourceItemId = "44444444444444444444444444444444" };

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyRefusal.LibraryWritesFiles, identification.Refusal);
        Assert.Equal(
            "Jellyfin's library “Колекції” saves artwork and metadata next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.RemoteSearches);
    }

    [Fact]
    public async Task Identify_in_a_library_without_a_name_that_saves_beside_the_media_calls_it_unnamed()
    {
        //Arrange
        // MAM-47 — "(unnamed)" is the app's word, so the sentence has a key of its own (library.writes-files-unnamed).
        _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["SaveLocalMetadata"] = true;
        _jellyfin.Server.VirtualFolders.OfType<JsonObject>().Single(folder => (string?)folder["Name"] == CartoonsLibrary).Remove("Name");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyRefusal.LibraryWritesFiles, identification.Refusal);
        Assert.Equal(
            "Jellyfin's library “(unnamed)” saves artwork and metadata next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.RemoteSearches);
    }

    [Fact]
    public async Task Identify_names_everything_the_library_saves_beside_the_media()
    {
        //Arrange
        var options = _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot);
        options["SaveLocalMetadata"] = true;
        options["MetadataSavers"] = new JsonArray("Nfo");
        options["SaveTrickplayWithMedia"] = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            $"Jellyfin's library “{CartoonsLibrary}” saves artwork and metadata, NFO files and trickplay images next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
            TestTexts.En(result.Value!.Sentence));
    }

    [Theory]
    [InlineData("no options", "the library's options are missing")]
    [InlineData("no SaveLocalMetadata", "its SaveLocalMetadata setting is missing or isn't a yes or a no")]
    [InlineData("a text SaveLocalMetadata", "its SaveLocalMetadata setting is missing or isn't a yes or a no")]
    [InlineData("MetadataSavers an object", "its MetadataSavers setting isn't a list")]
    [InlineData("no SaveTrickplayWithMedia", "its SaveTrickplayWithMedia setting is missing or isn't a yes or a no")]
    [InlineData("SubtitleDownloadLanguages an object", "its SubtitleDownloadLanguages setting isn't a list")]
    [InlineData("no path", "none of Jellyfin's libraries holds the item's path")]
    [InlineData("a path outside every library", "none of Jellyfin's libraries holds the item's path")]
    public async Task Identify_with_library_options_it_cannot_read_is_refused_unknown(string problem, string expectedReason)
    {
        //Arrange
        var folder = _jellyfin.Server.VirtualFolders.OfType<JsonObject>().Single(candidate => (string?)candidate["Name"] == CartoonsLibrary);
        var options = folder["LibraryOptions"]!.AsObject();
        switch (problem)
        {
            case "no options":
                folder.Remove("LibraryOptions");
                break;
            case "no SaveLocalMetadata":
                options.Remove("SaveLocalMetadata");
                break;
            case "a text SaveLocalMetadata":
                options["SaveLocalMetadata"] = "false";
                break;
            case "MetadataSavers an object":
                options["MetadataSavers"] = new JsonObject();
                break;
            case "no SaveTrickplayWithMedia":
                options.Remove("SaveTrickplayWithMedia");
                break;
            case "SubtitleDownloadLanguages an object":
                options["SubtitleDownloadLanguages"] = new JsonObject();
                break;
            case "no path":
                _jellyfin.Film.Remove("Path");
                break;
            default:
                _jellyfin.Film["Path"] = "/somewhere/else/Film.mkv";
                break;
        }

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // Fail closed: what the app can't tell, it takes as unsafe.
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Refused, identification.Outcome);
        Assert.Equal(LibraryIdentifyRefusal.LibraryUnknown, identification.Refusal);
        Assert.Equal(
            $"The app couldn't tell what this item's Jellyfin library saves next to the media ({expectedReason}), so it left the item alone.",
            TestTexts.En(identification.Sentence));
        Assert.Equal(3, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.Server.RemoteSearches);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Theory]
    [InlineData("savers empty")]
    [InlineData("savers absent")]
    [InlineData("savers null")]
    [InlineData("subtitle languages empty")]
    [InlineData("subtitle languages absent")]
    [InlineData("subtitle languages with the subtitles kept apart")]
    public async Task Identify_in_a_library_that_saves_nothing_beside_the_media_goes_on(string options)
    {
        //Arrange
        var libraryOptions = _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot);
        switch (options)
        {
            case "savers empty":
                libraryOptions["MetadataSavers"] = new JsonArray();
                break;
            case "savers absent":
                libraryOptions.Remove("MetadataSavers");
                break;
            case "savers null":
                libraryOptions["MetadataSavers"] = null;
                break;
            case "subtitle languages empty":
                libraryOptions["SubtitleDownloadLanguages"] = new JsonArray();
                libraryOptions["SaveSubtitlesWithMedia"] = true;
                break;
            case "subtitle languages absent":
                libraryOptions.Remove("SubtitleDownloadLanguages");
                break;
            default:
                libraryOptions["SubtitleDownloadLanguages"] = new JsonArray("eng");
                libraryOptions["SaveSubtitlesWithMedia"] = false;
                break;
        }

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_refuses_when_any_library_holding_the_items_path_saves_beside_the_media()
    {
        //Arrange
        // A second library rooted at a folder inside the first: the path is under both, and every one must be safe.
        AddLibrary("Nested", [$"{JellyfinSamples.CartoonsRoot}/Tom & Jerry e001-e161 (1940-1967)"], saveLocalMetadata: true);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyRefusal.LibraryWritesFiles, identification.Refusal);
        Assert.Equal(
            "Jellyfin's library “Nested” saves artwork and metadata next to the media, so identifying this item could change files in your media folders; the app left it alone. Switch that off in the library's settings in Jellyfin, then retry.",
            TestTexts.En(identification.Sentence));
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_goes_on_when_every_library_holding_the_items_path_is_safe_and_uses_the_first_language_it_finds()
    {
        //Arrange
        // The cartoon library has no language; the nested one has: the first matched library with a language gives it, with
        // its own country.
        AddLibrary("Nested", [$"{JellyfinSamples.CartoonsRoot}/Tom & Jerry e001-e161 (1940-1967)"], saveLocalMetadata: false, language: "de", country: "DE");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Contains("\"MetadataLanguage\":\"de\",\"MetadataCountryCode\":\"DE\"", Assert.Single(_jellyfin.SearchRequests()).Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\\media\\cartoons\\Tom & Jerry e001-e161 (1940-1967)\\Film.mkv")]
    [InlineData("/MEDIA/Cartoons/Film.mkv")]
    [InlineData("/media/cartoons/")]
    public async Task Identify_matches_the_items_path_to_a_library_location_in_any_letter_case_and_with_backslashes(string path)
    {
        //Arrange
        // A false match only adds a library to check: here it is the one that makes the verdict unsafe, so the match is seen.
        _jellyfin.Film["Path"] = path;
        _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["SaveLocalMetadata"] = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyRefusal.LibraryWritesFiles, result.Value!.Refusal);
    }

    [Fact]
    public async Task Identify_does_not_match_a_library_whose_location_is_only_the_start_of_a_folders_name()
    {
        //Arrange
        // "/media/cart" is not above "/media/cartoons/…": a prefix of a name is not a folder above.
        AddLibrary("Cart", ["/media/cart"], saveLocalMetadata: true);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_names_the_library_cleaned_and_with_the_key_masked()
    {
        //Arrange
        _jellyfin.Server.VirtualFolders.OfType<JsonObject>().Single(candidate => (string?)candidate["Name"] == CartoonsLibrary)["Name"] =
            $"Cartoons\u0007 {ApiKey}";
        _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot)["SaveLocalMetadata"] = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.StartsWith("Jellyfin's library “Cartoons ***” saves", TestTexts.En(result.Value!.Sentence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identify_whose_libraries_jellyfin_refuses_to_list_fails_before_any_search()
    {
        //Arrange
        _jellyfin.Server.Intercept = request => request.RequestUri!.AbsolutePath == "/Library/VirtualFolders" ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal(
            "Jellyfin accepted the key but won't let it read the library (403 Forbidden). Use a key from Dashboard → API Keys, not a user's access token.",
            TestTexts.En(result.Error));
        Assert.Null(result.Value);
        Assert.Empty(_jellyfin.Server.RemoteSearches);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Adds a library with the options of a safe one — nothing saved beside the media but what is asked for.</summary>
    private void AddLibrary(string name, string[] locations, bool saveLocalMetadata, string language = "", string country = "")
    {
        var options = _jellyfin.Server.LibraryOptionsOf(JellyfinSamples.CartoonsRoot).DeepClone().AsObject();
        options["SaveLocalMetadata"] = saveLocalMetadata;
        options["PreferredMetadataLanguage"] = language;
        options["MetadataCountryCode"] = country;
        _jellyfin.Server.VirtualFolders.Add(new JsonObject
        {
            ["Name"] = name,
            ["Locations"] = new JsonArray([.. locations.Select(location => (JsonNode)location)]),
            ["ItemId"] = Guid.NewGuid().ToString("N"),
            ["LibraryOptions"] = options,
        });
    }
}
