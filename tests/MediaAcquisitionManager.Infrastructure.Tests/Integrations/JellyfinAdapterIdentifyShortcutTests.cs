using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using static MediaAcquisitionManager.TestInfrastructure.Integrations.JellyfinIdentifyHarness;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — the idempotence shortcut of Jellyfin's Identify: an item that already shows the search result is not sent
// Apply, which would delete its images for nothing — and the old "Search for missing metadata" the owner pressed for every link
// leaves exactly an item's name, year, ids and a poster, so the shortcut also compares the overview, which that refresh leaves
// to whichever provider filled it. Five facts decide it (name, year, the id under its key, a primary image when the result has
// one, the overview when the result has one); anything that differs, or isn't there, means Apply: the safe direction. The
// recorded items after the live check's three Applies and the rows their searches answered are the proof the rule fires for what
// Identify really leaves; the changes to them are the proof it doesn't for what it didn't.
public sealed class JellyfinAdapterIdentifyShortcutTests : IDisposable
{
    private readonly JellyfinIdentifyHarness _jellyfin = new();

    public void Dispose() => _jellyfin.Dispose();

    [Theory]
    [InlineData("film", "Imdb tt0056164|Tmdb 223021")]
    [InlineData("show", "Imdb tt0892700|TmdbTv 7869|Tvdb 85075")]
    [InlineData("episode-movie", "Imdb tt9103946")]
    public async Task Shortcut_fires_for_the_recorded_items_after_their_identify_and_sends_no_apply_and_no_second_read(string flow, string expectedIds)
    {
        //Arrange
        // The three items the live check identified, read again after their Apply (item-after-identify.json,
        // series-item-after-identify.json, episode-movie-after-identify.json), against the rows their searches answered. The
        // film and the show have equal overviews after the whitespace is normalised; the Open Movie Database row has none.
        LibraryIdentifyRequest request;
        switch (flow)
        {
            case "film":
                Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());
                request = FilmRequest();
                break;
            case "show":
                Hold(_jellyfin.Server.Series, JellyfinSamples.SeriesItemAfterIdentify());
                request = ShowRequest();
                break;
            default:
                Hold(_jellyfin.Server.Movies, JellyfinSamples.EpisodeMovieAfterIdentify());
                request = EpisodeMovieRequest();
                break;
        }

        //Act
        var result = await _jellyfin.IdentifyAsync(request, TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, identification.Outcome);
        Assert.Null(identification.Refusal);
        Assert.False(identification.Sent);
        Assert.Null(identification.Sentence);
        Assert.Equal(expectedIds.Split('|'), Describe(identification.IdsBefore));
        Assert.Equal(Describe(identification.IdsBefore), Describe(identification.IdsAfter));
        // The users, the item, the libraries and the search: no Apply, and no second read of the item.
        Assert.Equal(4, _jellyfin.Http.Requests.Count);
        Assert.Empty(_jellyfin.ApplyRequests());
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_whose_item_already_shows_the_row_is_already_identified_whatever_the_ids_the_other_providers_added()
    {
        //Arrange
        Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(protectHeldIds: true), TestContext.Current.CancellationToken);

        //Assert
        // Its Tvdb ids, which other providers re-added, are not what the identification is by, and held ids that agree
        // conflict with nothing.
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_a_show_that_before_any_identify_already_shows_tmdbs_name_year_id_poster_and_overview_is_already_identified()
    {
        //Arrange
        // The Penguins of Madagascar as the live check read them BEFORE its Apply: Jellyfin's own refresh had already given
        // the show TMDb's name and text, so nothing in the item tells it from one Identify left — an Apply would only delete
        // and fetch its images again and, for a show, touch every episode (recorded: each lost its TvMaze id). Where its
        // images came from is the one thing the app can't see, and the shortcut does not claim to: it is there to avoid that cost.
        var item = Hold(_jellyfin.Server.Series, JellyfinSamples.SeriesItemAfterIdentify());
        item["Etag"] = "before-identify-etag";
        item["ProviderIds"] = new JsonObject { ["Imdb"] = "tt0892700", ["Tmdb"] = "7869", ["TvMaze"] = "4037", ["Tvdb"] = "85075" };

        //Act
        var result = await _jellyfin.IdentifyAsync(ShowRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_item_whose_name_year_and_ids_match_but_whose_overview_differs_applies()
    {
        //Arrange
        // What Jellyfin's old "Search for missing metadata" leaves: TMDb's name, year, ids and a poster, and the text another
        // provider (TVDB, AniList) supplied.
        var item = Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());
        item["Overview"] = "A text the TVDB gave this film.";

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
        Assert.StartsWith("Tom and Jerry are sleeping outside", (string?)item["Overview"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("an empty overview")]
    [InlineData("no overview")]
    public async Task Identify_of_an_item_whose_overview_is_empty_or_absent_applies(string overview)
    {
        //Arrange
        var item = Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());
        if (overview == "an empty overview")
            item["Overview"] = "";
        else
            item.Remove("Overview");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Theory]
    [InlineData("the name")]
    [InlineData("the year")]
    [InlineData("the id")]
    [InlineData("no id")]
    [InlineData("the primary image")]
    [InlineData("the overview")]
    public async Task Identify_applies_when_the_item_differs_from_the_row_in_name_year_id_primary_image_or_overview(string differs)
    {
        //Arrange
        var item = Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());
        switch (differs)
        {
            case "the name":
                item["Name"] = "Another name";
                break;
            case "the year":
                item["ProductionYear"] = 1963;
                break;
            case "the id":
                item["ProviderIds"] = new JsonObject { ["Tmdb"] = "999", ["Imdb"] = "tt0056164" };
                break;
            case "no id":
                item["ProviderIds"] = new JsonObject { ["Imdb"] = "tt0056164" };
                break;
            case "the primary image":
                item["ImageTags"] = new JsonObject();
                break;
            default:
                item["Overview"] = "Another text.";
                break;
        }

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // Any one of the five facts missing is a doubt, and a doubt is an Apply.
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.True(result.Value.Sent);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_item_without_a_primary_image_whose_row_has_no_image_url_is_already_identified_with_no_apply()
    {
        //Arrange
        // An item TMDb has no poster for can still be recognised: without this rule every click would Apply it again, and
        // each Apply deletes its images.
        Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify())["ImageTags"] = new JsonObject();
        _jellyfin.Server.Matches[0].Row.Remove("ImageUrl");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_of_an_item_without_a_primary_image_whose_row_has_an_image_url_applies()
    {
        //Arrange
        Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify())["ImageTags"] = new JsonObject();

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Theory]
    [InlineData("a row without an overview")]
    [InlineData("a row with a blank overview")]
    public async Task Identify_leaves_the_overview_out_of_the_shortcut_when_the_row_has_none(string rowOverview)
    {
        //Arrange
        // Every Open Movie Database row of an episode has none: the item's text is whatever it holds, and nothing to compare.
        var item = Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());
        item["Overview"] = "Text from somewhere else.";
        if (rowOverview == "a row without an overview")
            _jellyfin.Server.Matches[0].Row.Remove("Overview");
        else
            _jellyfin.Server.Matches[0].Row["Overview"] = "  ";

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
        Assert.Empty(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_compares_the_name_ignoring_case_and_surrounding_space()
    {
        //Arrange
        Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify())["Name"] = "  LANDING stripling ";

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_compares_the_overview_ignoring_how_whitespace_is_laid_out_but_not_the_letters()
    {
        //Arrange
        var item = Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify());
        var overview = (string)item["Overview"]!;
        item["Overview"] = "  " + overview.Replace(" ", "  \n ", StringComparison.Ordinal) + "\t";

        //Act
        var spaced = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);
        item["Overview"] = overview.ToUpperInvariant();
        var shouted = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, spaced.Value!.Outcome);
        // Ordinal and case-sensitive: another letter case is another text.
        Assert.Equal(LibraryIdentifyOutcome.Identified, shouted.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_ignores_the_year_when_the_row_has_none()
    {
        //Arrange
        Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify())["ProductionYear"] = 1999;
        _jellyfin.Server.Matches[0].Row.Remove("ProductionYear");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_reads_the_held_id_in_any_letter_case_of_its_key_and_trimmed()
    {
        //Arrange
        Hold(_jellyfin.Server.Movies, JellyfinSamples.ItemAfterIdentify())["ProviderIds"] =
            new JsonObject { ["tmdb"] = " 223021 ", ["IMDB"] = "tt0056164" };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.AlreadyIdentified, result.Value!.Outcome);
        Assert.Equal(["Imdb tt0056164", "Tmdb 223021"], Describe(result.Value.IdsBefore));
    }

    [Fact]
    public async Task Identify_of_an_item_that_the_old_refresh_left_with_the_ids_but_not_the_name_applies()
    {
        //Arrange
        // The item as it is before its Identify but holding the ids the app wrote: the name is the file's.
        _jellyfin.Film["ProviderIds"] = new JsonObject { ["Tmdb"] = "223021", ["Imdb"] = "tt0056164" };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Single(_jellyfin.Server.Applied);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Makes the fake hold <paramref name="item"/> in place of the item of its id, and returns it.</summary>
    private static JsonObject Hold(List<JsonObject> list, JsonObject item)
    {
        var index = list.FindIndex(held => string.Equals((string?)held["Id"], (string?)item["Id"], StringComparison.Ordinal));
        list[index] = item;
        return item;
    }
}
