using System.Net;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Integrations.JellyfinIdentifyHarness;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 (0.2.1) — what the item read again must show before an Identify is "identified" (design point g): Jellyfin's 204 says
// nothing of what Apply did, so the item is read again and compared — its name (strictly: the row was searched in the library's
// own language, so another name means another provider supplied it), its year, the id under its key, a change (Apply always
// changes the Etag; an unchanged item can't be identified) and a primary image when the result has one, because Apply deletes
// the images before the providers run. Never the overview: a re-read that failed on the spelling of a text would send the
// owner's Retry into a second destructive Apply. A partial result — the ids replaced, the metadata not — is never "identified".
public sealed class JellyfinAdapterIdentifyConfirmationTests : IDisposable
{
    private const string NotConfirmedProblem =
        "Jellyfin took the match, but the item read again doesn't show it (its name, year or id): it may be locked, or its metadata sources failed. Its own log says why; Retry checks again.";

    private readonly JellyfinIdentifyHarness _jellyfin = new();

    public void Dispose() => _jellyfin.Dispose();

    [Theory]
    [InlineData("the name", "Imdb tt0056164|Tmdb 223021")]
    [InlineData("the year", "Imdb tt0056164|Tmdb 223021")]
    [InlineData("the id", "Imdb tt0056164|Tmdb 999")]
    [InlineData("no id", "Imdb tt0056164")]
    public async Task Identify_whose_apply_took_but_shows_another_name_year_or_id_is_not_confirmed(string shows, string expectedIdsAfter)
    {
        //Arrange
        // Jellyfin answers 204 and the item read again holds something else: a metadata fetcher ordered before TMDb gave
        // another name, a year, or another id.
        _jellyfin.Server.AfterApply = _ =>
        {
            switch (shows)
            {
                case "the name":
                    _jellyfin.Film["Name"] = "Another name";
                    break;
                case "the year":
                    _jellyfin.Film["ProductionYear"] = 1999;
                    break;
                case "the id":
                    _jellyfin.Film["ProviderIds"] = new JsonObject { ["Tmdb"] = "999", ["Imdb"] = "tt0056164" };
                    break;
                default:
                    _jellyfin.Film["ProviderIds"] = new JsonObject { ["Imdb"] = "tt0056164" };
                    break;
            }

            return null;
        };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Equal(NotConfirmedProblem, TestTexts.En(identification.Sentence));
        Assert.Empty(identification.IdsBefore);
        Assert.Equal(expectedIdsAfter.Split('|'), Describe(identification.IdsAfter));
        // Sent once, never again: Retry reads first.
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(6, _jellyfin.Http.Requests.Count);
    }

    [Fact]
    public async Task Identify_whose_item_changed_nothing_is_not_confirmed_even_when_its_name_year_and_id_already_match()
    {
        //Arrange
        // An item that shows TMDb's name, year, ids and a poster but another provider's text is Applied; Jellyfin answers 204
        // and keeps it as it was, its Etag and primary image tag included: nothing changed, so nothing was identified.
        Replace(JellyfinSamples.ItemAfterIdentify())["Overview"] = "A text the TVDB gave this film.";
        _jellyfin.Server.IgnoresApply = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Equal(NotConfirmedProblem, TestTexts.En(identification.Sentence));
        Assert.Single(_jellyfin.Server.Applied);
    }

    [Fact]
    public async Task Identify_is_confirmed_by_a_new_primary_image_tag_when_the_etag_is_the_same()
    {
        //Arrange
        var item = Replace(JellyfinSamples.ItemAfterIdentify());
        item["Overview"] = "A text the TVDB gave this film.";
        var etag = (string?)item["Etag"];
        _jellyfin.Server.AfterApply = _ =>
        {
            item["Etag"] = etag;
            return null;
        };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_is_confirmed_by_a_new_etag_when_the_primary_image_tag_is_the_same()
    {
        //Arrange
        var item = Replace(JellyfinSamples.ItemAfterIdentify());
        item["Overview"] = "A text the TVDB gave this film.";
        var primary = (string?)item["ImageTags"]!["Primary"];
        _jellyfin.Server.AfterApply = _ =>
        {
            item["ImageTags"]!["Primary"] = primary;
            return null;
        };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_that_took_only_the_ids_is_not_confirmed()
    {
        //Arrange
        // The partial result of design point g: the ids replaced, the metadata not (TMDb failing after the images were
        // deleted). The item keeps the file's name and loses its images.
        _jellyfin.Server.AppliesIdsOnly = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.Equal(NotConfirmedProblem, TestTexts.En(identification.Sentence));
        Assert.Equal(["Imdb tt0056164", "Tmdb 223021"], Describe(identification.IdsAfter));
        Assert.Equal("120. Landing Stripling", (string?)_jellyfin.Film["Name"]);
    }

    [Fact]
    public async Task Identify_that_took_only_the_ids_of_an_item_whose_name_already_equals_the_row_is_not_confirmed()
    {
        //Arrange
        // The item already shows TMDb's name, year and ids but another provider's text: it is Applied, and the partial result
        // replaces the ids, deletes the images and changes the Etag, exactly what a real partial Apply leaves. Name, year, id
        // and Etag all pass; the poster Apply deleted is what fails the confirmation.
        Replace(JellyfinSamples.ItemAfterIdentify())["Overview"] = "A text the TVDB gave this film.";
        _jellyfin.Server.AppliesIdsOnly = true;

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.NotConfirmed, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Empty(_jellyfin.Film["ImageTags"]!.AsObject());
    }

    [Fact]
    public async Task Identify_does_not_look_for_a_primary_image_when_the_row_has_no_image_url()
    {
        //Arrange
        // TMDb has no poster for it: Apply deletes the item's images and fetches none, and that is a match, not a loss.
        _jellyfin.Server.Matches[0].Row.Remove("ImageUrl");

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        Assert.Empty(_jellyfin.Film["ImageTags"]!.AsObject());
    }

    [Fact]
    public async Task Identify_compares_the_name_after_apply_ignoring_case_and_surrounding_space()
    {
        //Arrange
        _jellyfin.Server.AfterApply = _ =>
        {
            _jellyfin.Film["Name"] = " landing STRIPLING  ";
            return null;
        };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_does_not_compare_the_overview_after_apply()
    {
        //Arrange
        // A re-read that failed on the spelling of a text would send the owner's Retry into a second Apply.
        _jellyfin.Server.AfterApply = _ =>
        {
            _jellyfin.Film["Overview"] = "Another provider's text.";
            return null;
        };

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
    }

    [Fact]
    public async Task Identify_of_a_show_whose_item_shows_the_match_leaves_its_episodes_to_jellyfin()
    {
        //Arrange
        // Apply of a series re-fetches every episode (about a second each, recorded); the series item is what is confirmed.
        _jellyfin.Server.Episodes.Add(JellyfinSamples.EpisodeWith(
            "33333333333333333333333333333333", "Episode", JellyfinSamples.PenguinsId, 1, 1, $"{JellyfinSamples.CartoonsRoot}/Penguins/e01.mkv", "tt0000001", "1"));
        _jellyfin.Server.Episodes[0]["ProviderIds"]!["TvMaze"] = "9";

        //Act
        var result = await _jellyfin.IdentifyAsync(ShowRequest(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(LibraryIdentifyOutcome.Identified, result.Value!.Outcome);
        // As recorded live: the episodes lost their TvMaze id, and kept the rest.
        Assert.Equal("""{"Imdb":"tt0000001","Tvdb":"1"}""", _jellyfin.Server.Episodes[0]["ProviderIds"]!.ToJsonString());
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Identify_whose_apply_jellyfin_failed_on_is_identified_when_the_item_read_again_shows_the_match(int statusCode)
    {
        //Arrange
        // Jellyfin failed while answering, after it had applied the result: the re-read is the truth, whatever the answer was.
        _jellyfin.Server.AfterApply = _ => new HttpResponseMessage((HttpStatusCode)statusCode);

        //Act
        var result = await _jellyfin.IdentifyAsync(FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        var identification = result.Value!;
        Assert.Equal(LibraryIdentifyOutcome.Identified, identification.Outcome);
        Assert.True(identification.Sent);
        Assert.Null(identification.Sentence);
        Assert.Single(_jellyfin.ApplyRequests());
        Assert.Equal(6, _jellyfin.Http.Requests.Count);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Makes the fake hold <paramref name="item"/> in place of the film, and returns it.</summary>
    private JsonObject Replace(JsonObject item)
    {
        _jellyfin.Server.Movies[_jellyfin.Server.Movies.IndexOf(_jellyfin.Film)] = item;
        return item;
    }
}
