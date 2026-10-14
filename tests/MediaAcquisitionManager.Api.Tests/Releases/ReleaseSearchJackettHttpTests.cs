using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Releases;

// MAM-30 — a release search through Jackett instead of Prowlarr (Vasyl's decision 5), over real HTTP and the real Torznab
// adapter with Jackett's recorded Toloka answers: its releases answered as Prowlarr's are, each stored with its link at
// Jackett's host root sealed without the key, and none of that link — its key, its token — in any answer; a TV search
// asked of it as of Prowlarr; a key it refuses said with where to copy the right one (D16).
public sealed class ReleaseSearchJackettHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/releases/search";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _jackett = null!;

    public ReleaseSearchJackettHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jackett = FakeTorznabServer.Jackett(JackettAddress, ApiKey);
        _jackett.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Search_answers_jacketts_releases_with_their_descriptions_and_categories_and_no_part_of_their_links()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var source = Assert.Single((await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Sources);
        Assert.Equal((jackett.Id, "Toloka", (IntegrationCallStatus?)IntegrationCallStatus.Succeeded, (string?)null), (source.IntegrationId, source.Name, source.Status, source.Error));
        // Jackett's 18 less the audio release and the trailer, which aren't in the movie categories asked.
        Assert.Equal(TorznabSamples.JackettItemCount - TorznabSamples.JackettNonMovieItemCount, source.Releases.Count);
        var first = source.Releases[0];
        Assert.Equal("Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", first.Title);
        Assert.Equal("Інтерстеллар / Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", first.Description);
        Assert.Equal(["2040", "100096"], first.Categories.Select(category => category.Id));
        Assert.Equal("https://toloka.to/t699400", first.SourcePageUrl);
        Assert.Equal((ReleaseKind.Movie, (int?)1080), (first.Kind, first.Claimed.Resolution));
        // Where to download it from never leaves the server (ADR 0004), Jackett's key and token least of all.
        foreach (var part in new[] { ApiKey, TorznabSamples.JackettPathToken, "jackett_apikey", "/dl/", "jackett.test" })
            Assert.DoesNotContain(part, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_stores_each_link_at_jacketts_host_root_sealed_to_its_row_with_the_keys_placeholder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(TorznabSamples.JackettItemCount - TorznabSamples.JackettNonMovieItemCount, stored.Count);
            var encryption = _factory.Resolve<IEncryptionService>();
            Assert.All(stored, release =>
            {
                Assert.True(encryption.TryDecrypt(release.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(release.Id), out var link));
                Assert.StartsWith(
                    $"http://jackett.test:9117/dl/toloka/?jackett_apikey=%7Bapi-key%7D&path={TorznabSamples.JackettPathToken}&file=Interstellar+(2014)",
                    link,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(ApiKey, link, StringComparison.Ordinal);
                Assert.Equal(release.SourcePageUrl, release.SourceItemId);
                Assert.False(string.IsNullOrEmpty(release.Description));
            });
        });
    }

    [Fact]
    public async Task Search_of_a_show_asks_jacketts_tv_search_as_prowlarrs_and_lists_its_seasons_as_tv()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        _jackett.SearchXml = TorznabSamples.Feed();
        _jackett.TvSearchXml = TorznabSamples.JackettTvSearch(JackettAddress, ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Yellowstone"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Sources);
        // The same t=tvsearch in the same categories as Prowlarr gets: the caps list the same ones on the same ids.
        Assert.Contains(
            _factory.TorznabHttp.Requests,
            request => request.Uri.AbsoluteUri == $"{JackettAddress}api?t=tvsearch&apikey={ApiKey}&q=Yellowstone&cat=5000,5040,5050,5060,5070,5080&limit=100");
        Assert.Equal(TorznabSamples.JackettTvItemCount - TorznabSamples.JackettNonTvItemCount, source.Releases.Count);
        Assert.Equal(TorznabSamples.TvFilmItemCount, source.Releases.Count(release => release.Kind == ReleaseKind.Movie));
        Assert.Equal(
            "Єллоустоун (Сезони 1-5) / Yellowstone (Seasons 1-5) (2018-2024) WEB-DLRip-AVC Ukr/Eng | Sub Eng",
            Assert.Single(source.Releases, release => release.Title == "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng").Description);
    }

    [Fact]
    public async Task Search_of_a_source_whose_key_jackett_refuses_says_so_with_where_to_copy_the_right_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: "a-key-jackett-refuses");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        // Jackett's recorded answer to a wrong key: HTTP 200 and Torznab's error 100, to the caps request.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var source = Assert.Single((await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Sources);
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, source.Status);
        Assert.Equal(
            "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.",
            source.Error);
        Assert.Empty(source.Releases);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
    }
}
