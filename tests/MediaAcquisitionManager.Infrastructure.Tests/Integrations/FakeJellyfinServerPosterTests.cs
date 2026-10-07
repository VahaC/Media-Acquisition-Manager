using System.Net;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-35 — the fake Jellyfin's image route, which every poster test stands on, answers as the live check of 2026-10-07
// recorded it (VendorSamples/Jellyfin/image-primary*-response.txt): the poster of an item that has one whatever tag is asked,
// a 404 for an item without one, a 400 for an id it doesn't hold — and a fake Identify changes which, as Jellyfin's does.
public sealed partial class FakeJellyfinServerPosterTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private readonly RecordingHttpHandler _handler = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };
    private readonly HttpClient _client;

    public FakeJellyfinServerPosterTests()
    {
        _server.Series.AddRange(JellyfinSamples.SeriesWithImages());
        _server.Episodes.AddRange(JellyfinSamples.EpisodesWithImages());
        _server.Arm(_handler);
        _client = new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl + "/") };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{ApiKey}\"");
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Poster_of_an_item_with_a_primary_image_is_the_recorded_answer_with_the_test_jpeg()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync(PosterPath(JellyfinSamples.YellowstoneId, JellyfinSamples.YellowstonePosterTag), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestImages.Jpeg(), await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        // The recorded headers, but the length of the bytes it really sends.
        Assert.Equal(TestImages.Jpeg().Length, response.Content.Headers.ContentLength);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal("\"7060953c5bc967a2e947ecc514b77a37\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Poster_answers_whatever_tag_is_asked_as_jellyfin_does()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync(PosterPath(JellyfinSamples.YellowstoneId, "0123456789abcdef0123456789abcdef"), TestContext.Current.CancellationToken);

        //Assert
        // Recorded: Jellyfin doesn't check the tag, it only versions the URL.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestImages.Jpeg(), await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Poster_of_an_item_without_a_primary_image_is_the_recorded_404()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync(PosterPath(JellyfinSamples.FollowTheMouseId, "01de7f6467277e81d5abec6fc8256006"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "\"Follow the Mouse does not have an image of type Primary\"",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Poster_of_an_id_no_list_holds_is_the_recorded_400()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync(PosterPath("00000000000000000000000000000000", "0123456789abcdef0123456789abcdef"), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Error processing request.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Apply_gives_the_item_a_primary_tag_of_letters_and_digits_and_the_route_serves_it()
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness();
        var before = (string?)jellyfin.Show["ImageTags"]!.AsObject()["Primary"];

        //Act
        await jellyfin.IdentifyAsync(JellyfinIdentifyHarness.ShowRequest(), TestContext.Current.CancellationToken);

        //Assert
        // series-item.json had no picture; the Apply gave it one, under a tag the sync takes (no dash, as Jellyfin's).
        Assert.Null(before);
        var tag = (string?)jellyfin.Show["ImageTags"]!["Primary"];
        Assert.Matches(TagPattern(), tag);
        Assert.Equal(HttpStatusCode.OK, PosterStatus(jellyfin.Server, JellyfinSamples.PenguinsId, tag!));
    }

    [Fact]
    public async Task Poster_after_an_apply_that_left_the_item_without_images_is_the_recorded_404()
    {
        //Arrange
        using var jellyfin = new JellyfinIdentifyHarness();
        jellyfin.Server.AppliesIdsOnly = true;
        var tag = (string)jellyfin.Film["ImageTags"]!["Primary"]!;
        var before = PosterStatus(jellyfin.Server, JellyfinSamples.LandingStriplingId, tag);

        //Act
        await jellyfin.IdentifyAsync(JellyfinIdentifyHarness.FilmRequest(), TestContext.Current.CancellationToken);

        //Assert
        // replaceAllImages deletes the images before the providers run; when they fail the item has none until the next one.
        Assert.Equal(HttpStatusCode.OK, before);
        Assert.Equal(HttpStatusCode.NotFound, PosterStatus(jellyfin.Server, JellyfinSamples.LandingStriplingId, tag));
    }

    private static string PosterPath(string itemId, string tag) => $"Items/{itemId}/Images/Primary?tag={tag}&format=Jpg&maxWidth=342&quality=90";

    // What the harness's server answers the poster of itemId, asked straight, with the key.
    private static HttpStatusCode PosterStatus(FakeJellyfinServer server, string itemId, string tag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(JellyfinIdentifyHarness.BaseUrl + "/"), PosterPath(itemId, tag)));
        request.Headers.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{JellyfinIdentifyHarness.ApiKey}\"");
        using var response = server.Answer(request);
        return response.StatusCode;
    }

    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}\z")]
    private static partial Regex TagPattern();
}
