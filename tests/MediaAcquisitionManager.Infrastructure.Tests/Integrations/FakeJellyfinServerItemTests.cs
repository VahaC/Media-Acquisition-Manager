using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-34 — the fake Jellyfin's one-item read, which every link test stands on, answers as the live check recorded it
// (VendorSamples/Jellyfin): the users with their policies, and an item read only as a user (400 without one, 404 for an id it
// doesn't hold). Jellyfin's Identify has its own class (FakeJellyfinServerIdentifyTests).
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
    public async Task Item_read_with_a_wrong_key_is_401()
    {
        //Arrange
        _server.ApiKey = "another-key";

        //Act
        var read = await _client.GetAsync($"Items/{JellyfinSamples.AliensTheatricalId}?userId={JellyfinSamples.AdministratorUserId}", TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    [Fact]
    public async Task A_post_of_an_item_or_a_refresh_is_unknown_to_the_fake()
    {
        //Arrange
        // Jellyfin's older ways to change an item — POST /Items/{id} with the item sent back, and POST /Items/{id}/Refresh —
        // are not what the app does since 0.2.1: they answer 404 here, so a regression that posts an item fails loudly.
        using var body = new StringContent(JellyfinSamples.Item().ToJsonString(), Encoding.UTF8, "application/json");

        //Act
        var update = await _client.PostAsync($"Items/{JellyfinSamples.LandingStriplingId}", body, TestContext.Current.CancellationToken);
        var refresh = await _client.PostAsync(
            $"Items/{JellyfinSamples.LandingStriplingId}/Refresh?metadataRefreshMode=FullRefresh", null, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (update.StatusCode, refresh.StatusCode));
    }
}
