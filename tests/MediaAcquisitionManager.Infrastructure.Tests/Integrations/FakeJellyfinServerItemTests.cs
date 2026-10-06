using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 — the fake Jellyfin's one-item read and write, which every link test stands on, answer as the live check
// recorded (VendorSamples/Jellyfin): the users with their policies, an item read only as a user (400 without one, 404 for
// an id it doesn't hold), and a write that applies the posted ProviderIds — a key left out or blank removed — so a later
// read or sync sees them; and the switches that make a write refused, taken but not applied, or its answer lost.
public sealed class FakeJellyfinServerItemTests : IDisposable
{
    private const string BaseUrl = "http://jellyfin.test:8096";
    private const string ApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private readonly RecordingHttpHandler _handler = new();
    private readonly FakeJellyfinServer _server = new(BaseUrl) { ApiKey = ApiKey };
    private readonly HttpClient _client;

    public FakeJellyfinServerItemTests()
    {
        _server.Arm(_handler);
        _client = new HttpClient(_handler) { BaseAddress = new Uri(BaseUrl + "/") };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"MediaBrowser Token=\"{ApiKey}\"");
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Users_answers_a_member_and_the_enabled_administrator()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync("Users", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsArray();
        Assert.Equal(
            [(JellyfinSamples.MemberUserId, false), (JellyfinSamples.AdministratorUserId, true)],
            users.Select(user => ((string)user!["Id"]!, (bool)user["Policy"]!["IsAdministrator"]!)));
        Assert.All(users, user => Assert.False((bool)user!["Policy"]!["IsDisabled"]!));
    }

    [Fact]
    public async Task Item_read_without_a_user_is_the_recorded_400()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync($"Items/{JellyfinSamples.AliensTheatricalId}", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Error processing request.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Item_read_of_an_id_no_list_holds_is_the_recorded_404()
    {
        //Arrange

        //Act
        var response = await _client.GetAsync($"Items/{Guid.NewGuid():N}?userId={JellyfinSamples.AdministratorUserId}", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("\"status\":404", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_read_answers_the_stored_item_by_its_id_in_any_form()
    {
        //Arrange
        var id = Guid.Parse(JellyfinSamples.AliensTheatricalId).ToString("D").ToUpperInvariant();

        //Act
        var response = await _client.GetAsync($"Items/{id}?userId={JellyfinSamples.AdministratorUserId}", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(JellyfinSamples.AliensTheatricalId, (string?)item["Id"]);
        Assert.Equal("679", (string?)item["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task Item_write_applies_the_posted_ids_so_a_later_read_and_list_see_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var posted = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        // A key changed, one sent blank, one left out (TmdbCollection): Jellyfin keeps only what it was sent.
        posted["ProviderIds"] = new JsonObject { ["Tmdb"] = "348", ["Imdb"] = "" };

        //Act
        var write = await PostAsync(JellyfinSamples.AliensTheatricalId, posted);
        var read = await _client.GetAsync($"Items/{JellyfinSamples.AliensTheatricalId}?userId={JellyfinSamples.AdministratorUserId}", cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        Assert.Empty(await write.Content.ReadAsStringAsync(cancellationToken));
        var item = JsonNode.Parse(await read.Content.ReadAsStringAsync(cancellationToken))!;
        Assert.Equal("""{"Tmdb":"348"}""", item["ProviderIds"]!.ToJsonString());
        Assert.Equal("""{"Tmdb":"348"}""", _server.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.AliensTheatricalId)["ProviderIds"]!.ToJsonString());
        // The body stays readable as sent.
        var request = Assert.Single(_handler.Requests, recorded => recorded.Method == HttpMethod.Post);
        Assert.Equal(posted.ToJsonString(), request.Body);
    }

    [Fact]
    public async Task Item_write_of_a_series_reaches_the_series_list()
    {
        //Arrange
        _server.Series.Add(JellyfinSamples.SeriesItem());
        var posted = JellyfinSamples.SeriesItem();
        posted["ProviderIds"] = new JsonObject { ["Tmdb"] = JellyfinSamples.PenguinsTmdbTvId };

        //Act
        var write = await PostAsync(JellyfinSamples.PenguinsId, posted);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        Assert.Equal("""{"Tmdb":"7869"}""", _server.Series.Single()["ProviderIds"]!.ToJsonString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_refused_write_answers_its_status_and_applies_nothing(HttpStatusCode refusal)
    {
        //Arrange
        _server.WriteRefusal = refusal;
        var posted = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        posted["ProviderIds"] = new JsonObject { ["Tmdb"] = "348" };

        //Act
        var write = await PostAsync(JellyfinSamples.AliensTheatricalId, posted);

        //Assert
        Assert.Equal(refusal, write.StatusCode);
        Assert.Equal("679", (string?)_server.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.AliensTheatricalId)["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task An_ignored_write_answers_204_and_applies_nothing()
    {
        //Arrange
        _server.IgnoresWrites = true;
        var posted = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        posted["ProviderIds"] = new JsonObject { ["Tmdb"] = "348" };

        //Act
        var write = await PostAsync(JellyfinSamples.AliensTheatricalId, posted);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        Assert.Equal("679", (string?)_server.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.AliensTheatricalId)["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task A_lost_answer_fails_the_request_after_the_write_landed()
    {
        //Arrange
        _server.AfterWrite = _ => throw new HttpRequestException("The answer never came.");
        var posted = JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId);
        posted["ProviderIds"] = new JsonObject { ["Tmdb"] = "348" };

        //Act
        var exception = await Record.ExceptionAsync(() => PostAsync(JellyfinSamples.AliensTheatricalId, posted));

        //Assert
        Assert.IsType<HttpRequestException>(exception);
        Assert.Equal("348", (string?)_server.Movies.Single(movie => (string?)movie["Id"] == JellyfinSamples.AliensTheatricalId)["ProviderIds"]!["Tmdb"]);
    }

    [Fact]
    public async Task Item_write_of_an_id_no_list_holds_is_the_recorded_404()
    {
        //Arrange

        //Act
        var write = await PostAsync($"{Guid.NewGuid():N}", JellyfinSamples.Item());

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task Item_read_and_write_with_a_wrong_key_are_401()
    {
        //Arrange
        _server.ApiKey = "another-key";

        //Act
        var read = await _client.GetAsync($"Items/{JellyfinSamples.AliensTheatricalId}?userId={JellyfinSamples.AdministratorUserId}", TestContext.Current.CancellationToken);
        var write = await PostAsync(JellyfinSamples.AliensTheatricalId, JellyfinSamples.Movie(JellyfinSamples.AliensTheatricalId));

        //Assert
        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized), (read.StatusCode, write.StatusCode));
    }

    private Task<HttpResponseMessage> PostAsync(string itemId, JsonObject item)
    {
        var content = new StringContent(item.ToJsonString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return _client.PostAsync($"Items/{itemId}", content, TestContext.Current.CancellationToken);
    }
}
