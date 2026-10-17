using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-46 — a metadata catalog's language on the Integrations page (POST and PUT /api/settings/integrations): a new
// catalog starts in the owner's saved language, else in the browser's (Accept-Language), else in en-US, each brought to
// the closest of TMDb's languages; a language named is kept as TMDb spells it, one TMDb hasn't is refused, and so is one
// for a type that is no catalog; a save that leaves it out keeps it, and so does one that sends the stored one back,
// even when TMDb no longer lists it; a change of an enabled catalog's language queues a computation of the
// recommendations and a preparation of the TMDb suggestions (CatalogLanguageChanged), asks TMDb nothing, is audited as
// any change, and has the newest releases matched to no work lately resolved again. Reading the catalog again is
// CatalogLanguageChangeHttpTests'.
public sealed class IntegrationsCatalogLanguageHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string NotACatalogError = "Jellyfin has no catalog language to choose. Leave catalogLanguage out.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IUserSeeder _users = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsCatalogLanguageHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _users = new UserSeeder(_factory.Scope);
        _owner = await _users.CreateAuthorizedOwner("owner");
        FakeTmdbServer.Arm(_factory.TmdbHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Create ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_of_a_catalog_without_a_language_starts_in_the_owners_saved_language_whatever_the_browser_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _users.SetLanguage(_owner.User, "uk");

        //Act
        var response = await CreateAsync(CatalogRequest(), acceptLanguage: "en-GB,en;q=0.9");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new IntegrationDto(
                body!.Id, "TMDb", "tmdb", "TMDb", IntegrationKind.Metadata, IsEnabled: true, FakeTmdbServer.BaseUrl,
                UserName: null, HasPassword: false, HasApiKey: true, [], now, DestinationKind: null, CatalogLanguage: "uk-UA"),
            body,
            strict: true);
        var read = await _factory.GetHttpRequest($"{BaseUrl}/{body.Id}", _owner.AccessToken);
        Assert.Equivalent(body, await read.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken), strict: true);
        await AssertStoredLanguageAsync("uk-UA", cancellationToken);
    }

    [Theory]
    [InlineData("en-GB,en;q=0.9", "en-GB")]
    [InlineData("uk-UA,uk;q=0.9,en-US;q=0.8", "uk-UA")]
    [InlineData("uk", "uk-UA")]
    [InlineData("de", "de-DE")]
    [InlineData("en", "en-US")]
    [InlineData("tlh", "en-US")]
    public async Task Create_of_a_catalog_without_a_language_by_an_owner_who_saved_none_starts_in_the_browsers(string acceptLanguage, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await CreateAsync(CatalogRequest(), acceptLanguage);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected, (await ReadIntegrationAsync(response, cancellationToken)).CatalogLanguage);
        await AssertStoredLanguageAsync(expected, cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_catalog_without_a_language_a_saved_one_or_a_browsers_starts_in_en_us()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await CreateAsync(CatalogRequest(), acceptLanguage: null);

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("en-US", (await ReadIntegrationAsync(response, cancellationToken)).CatalogLanguage);
        await AssertStoredLanguageAsync("en-US", cancellationToken);
    }

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    // Spelled as TMDb spells it, whatever the letter case sent.
    [InlineData("pt-br", "pt-BR")]
    [InlineData(" UK-ua ", "uk-UA")]
    public async Task Create_of_a_catalog_with_a_language_keeps_it_over_the_owners(string language, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.SetLanguage(_owner.User, "en");

        //Act
        var response = await CreateAsync(CatalogRequest(language), acceptLanguage: "en-US");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected, (await ReadIntegrationAsync(response, cancellationToken)).CatalogLanguage);
        await AssertStoredLanguageAsync(expected, cancellationToken);
    }

    [Theory]
    [InlineData("uk", "uk")]
    [InlineData("xx-XX", "xx-XX")]
    [InlineData("uk-UA&page=2", "uk-UA&page=2")]
    public async Task Create_of_a_catalog_with_a_language_tmdb_hasnt_is_400_with_the_sentence_and_creates_nothing(string language, string echoed)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await CreateAsync(CatalogRequest(language), acceptLanguage: null);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LanguageNotTmdbsError(echoed), await response.ReadErrorAsync(cancellationToken));
        await AssertNothingCreatedAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_an_integration_that_is_no_catalog_with_a_language_is_400_with_the_sentence_and_creates_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await CreateAsync(
            new CreateIntegrationRequest("jellyfin", "Living room", "http://127.0.0.1:9", ApiKey: Set(JellyfinApiKey), CatalogLanguage: "uk-UA"),
            acceptLanguage: null);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotACatalogError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingCreatedAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_an_integration_that_is_no_catalog_stores_no_language_whatever_the_owners()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _users.SetLanguage(_owner.User, "uk");

        //Act
        var response = await CreateAsync(
            new CreateIntegrationRequest("jellyfin", "Living room", "http://127.0.0.1:9", ApiKey: Set(JellyfinApiKey)), acceptLanguage: "uk-UA");

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await ReadIntegrationAsync(response, cancellationToken)).CatalogLanguage);
        await AssertStoredLanguageAsync(null, cancellationToken);
    }

    [Fact]
    public async Task Create_checks_the_language_after_the_credentials_and_before_whether_the_name_is_free()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);

        //Act
        var withoutKey = await CreateAsync(new CreateIntegrationRequest("tmdb", "TMDb", FakeTmdbServer.BaseUrl, CatalogLanguage: "xx-XX"), acceptLanguage: null);
        var nameTaken = await CreateAsync(CatalogRequest("xx-XX"), acceptLanguage: null);

        //Assert
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (withoutKey.StatusCode, nameTaken.StatusCode));
        Assert.Equal("TMDb needs an API key.", await withoutKey.ReadErrorAsync(cancellationToken));
        Assert.Equal(LanguageNotTmdbsError("xx-XX"), await nameTaken.ReadErrorAsync(cancellationToken));
    }

    // ── Update ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_without_a_language_keeps_the_stored_one_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("pt-BR");

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb renamed", FakeTmdbServer.BaseUrl));

        //Assert
        // A client that doesn't know the field can't change what the catalog is read in.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("TMDb renamed", (string?)"pt-BR"), (row.Name, row.CatalogLanguage));
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_that_changes_an_enabled_catalogs_language_queues_both_runs_asks_tmdb_nothing_and_audits_the_change()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA");

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb", FakeTmdbServer.BaseUrl, CatalogLanguage: "en-gb"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("en-GB", (await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken)).CatalogLanguage);
            // Queued by the app, with nobody named as having asked: the audit row says who changed the language.
            var run = await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (RecommendationRunStatus.Queued, RecommendationRunTrigger.CatalogLanguageChanged, (string?)null),
                (run.Status, run.Trigger, run.RequestedByUserName));
            var suggestions = await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (LibraryLinkSuggestionRunStatus.Queued, LibraryLinkSuggestionRunTrigger.CatalogLanguageChanged, (string?)null),
                (suggestions.Status, suggestions.Trigger, suggestions.RequestedByUserName));
            Assert.Equal([run.Id], DrainRecommendationQueue());
            Assert.Equal([suggestions.Id], DrainSuggestionQueue());
            var audit = await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (AuditAction.IntegrationUpdated, (Guid?)_owner.User.Id, (Guid?)catalog.Id),
                (audit.Action, audit.ActorUserId, audit.TargetId));
        });
        Assert.Empty(_factory.TmdbHttp.Requests);
    }

    [Theory]
    [InlineData("uk-UA")]
    // The same language in another letter case is the same language.
    [InlineData("UK-ua")]
    public async Task Update_that_sends_the_stored_language_again_changes_nothing_records_nothing_and_queues_nothing(string language)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA");

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb", FakeTmdbServer.BaseUrl, CatalogLanguage: language));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("uk-UA", (await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken)).CatalogLanguage);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_that_sends_back_a_stored_language_tmdb_no_longer_lists_keeps_it_and_saves_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A tag TMDb has dropped from its list since it was chosen, or a row edited by hand.
        var catalog = await ConnectCatalogAsync("xx-YY");

        //Act
        // The edit dialog sends the stored language with every save.
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb renamed", FakeTmdbServer.BaseUrl, CatalogLanguage: "xx-YY"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("TMDb renamed", (string?)"xx-YY"), (row.Name, row.CatalogLanguage));
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
        });
    }

    [Theory]
    [InlineData("en-US", true)]
    [InlineData("uk-UA", false)]
    public async Task Update_that_changes_the_language_has_the_newest_releases_matched_to_no_work_lately_resolved_again(string language, bool reopened)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA");
        var toloka = await _integrations.Create("torznab", "Toloka", apiKey: JellyfinApiKey);
        var work = await new CatalogWorkSeeder(_factory.Scope).Create(MediaWorkKind.Movie, "129", "Віднесені привидами", catalog: catalog);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var feed = new SourceFeedEntrySeeder(_factory.Scope);
        var unmatched = await feed.Create(toloka, "Віднесені привидами / Sen to Chihiro no kamikakushi (2001)", resolvedTo: null, seenUtc: now.AddDays(-2));
        // First seen 15 days ago: past what the recommendations count as new, whatever it turns out to be.
        var older = await feed.Create(toloka, "Щось давнє (2001)", resolvedTo: null, seenUtc: now.AddDays(-15));
        var matched = await feed.Create(toloka, "Віднесені привидами (2001)", work, seenUtc: now.AddDays(-2));
        var resolvedUtc = now.AddDays(-1);
        await _factory.WithDbAsync(db => db.SourceFeedEntries
            .Where(entry => entry.CatalogWorkId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.ResolvedUtc, (DateTime?)resolvedUtc), cancellationToken));

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb renamed", FakeTmdbServer.BaseUrl, CatalogLanguage: language));

        //Assert
        // Matched to no work in the old language, it is resolved again in the new one at the feed's next read.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var entries = await db.SourceFeedEntries.AsNoTracking().ToDictionaryAsync(entry => entry.Id, cancellationToken);
            Assert.Equal(reopened ? null : (DateTime?)resolvedUtc, entries[unmatched.Id].ResolvedUtc);
            Assert.Equal((resolvedUtc, (Guid?)null), (entries[older.Id].ResolvedUtc!.Value, entries[older.Id].CatalogWorkId));
            Assert.Equal((now.AddDays(-2), (Guid?)work.Id), (entries[matched.Id].ResolvedUtc!.Value, entries[matched.Id].CatalogWorkId));
        });
    }

    [Fact]
    public async Task Update_that_changes_a_switched_off_catalogs_language_saves_it_and_queues_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA", isEnabled: false);

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb", FakeTmdbServer.BaseUrl, CatalogLanguage: "en-US"));

        //Assert
        // Nothing reads a catalog that is off; switched back on, it is read in the language it has then.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("en-US", (await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken)).CatalogLanguage);
            Assert.Equal(AuditAction.IntegrationUpdated, (await db.AuditEvents.AsNoTracking().SingleAsync(cancellationToken)).Action);
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
            Assert.Equal(0, await db.LibraryLinkSuggestionRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_that_switches_a_catalog_back_on_in_another_language_queues_the_runs_as_a_language_change()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA", isEnabled: false);

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb", FakeTmdbServer.BaseUrl, IsEnabled: true, CatalogLanguage: "en-US"));

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(RecommendationRunTrigger.CatalogLanguageChanged, (await db.RecommendationRuns.AsNoTracking().SingleAsync(cancellationToken)).Trigger);
            Assert.Equal(
                LibraryLinkSuggestionRunTrigger.CatalogLanguageChanged,
                (await db.LibraryLinkSuggestionRuns.AsNoTracking().SingleAsync(cancellationToken)).Trigger);
        });
    }

    [Fact]
    public async Task Update_with_a_language_tmdb_hasnt_is_400_with_the_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var catalog = await ConnectCatalogAsync("uk-UA");

        //Act
        var response = await UpdateAsync(catalog, new UpdateIntegrationRequest("TMDb renamed", FakeTmdbServer.BaseUrl, CatalogLanguage: "ukrainian"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LanguageNotTmdbsError("ukrainian"), await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("TMDb", (string?)"uk-UA"), (row.Name, row.CatalogLanguage));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Update_of_an_integration_that_is_no_catalog_with_a_language_is_400_with_the_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        var response = await UpdateAsync(jellyfin, new UpdateIntegrationRequest("Living room", "http://127.0.0.1:9", CatalogLanguage: "uk-UA"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotACatalogError, await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Null((await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken)).CatalogLanguage);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    // ── List ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_carries_a_catalogs_language_and_none_for_any_other_integration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ConnectCatalogAsync("en-GB");
        await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IntegrationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(
            [("Living room", (string?)null), ("TMDb", "en-GB")],
            body!.Items.Select(integration => (integration.Name, integration.CatalogLanguage)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string LanguageNotTmdbsError(string echoed) =>
        $"“{echoed}” is not one of TMDb's catalog languages. Use one of the tags its adapter type lists, such as en-US or uk-UA.";

    private static CreateIntegrationRequest CatalogRequest(string? catalogLanguage = null) =>
        new("tmdb", "TMDb", FakeTmdbServer.BaseUrl, ApiKey: Set(FakeTmdbServer.Token), CatalogLanguage: catalogLanguage);

    private static SecretValueUpsert Set(string value) => new(SecretUpsertMode.Set, value);

    private Task<IntegrationConnectionEntity> ConnectCatalogAsync(string catalogLanguage, bool isEnabled = true) =>
        _integrations.Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token, isEnabled: isEnabled, catalogLanguage: catalogLanguage);

    /// <summary>The owner's create, sent from a browser whose <c>Accept-Language</c> is <paramref name="acceptLanguage"/>
    /// (none when <c>null</c>).</summary>
    private Task<HttpResponseMessage> CreateAsync(CreateIntegrationRequest body, string? acceptLanguage)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(body, options: MediaAcquisitionManagerApiFactory.JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _owner.AccessToken);
        if (acceptLanguage is not null)
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        return _factory.SendHttpRequest(request);
    }

    private Task<HttpResponseMessage> UpdateAsync(IntegrationConnectionEntity integration, UpdateIntegrationRequest body) =>
        _factory.PutHttpRequest($"{BaseUrl}/{integration.Id}", _owner.AccessToken, body);

    private static async Task<IntegrationDto> ReadIntegrationAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private async Task AssertStoredLanguageAsync(string? expected, CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
            Assert.Equal(expected, (await db.IntegrationConnections.AsNoTracking().SingleAsync(cancellationToken)).CatalogLanguage));

    private async Task AssertNothingCreatedAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.IntegrationConnections.CountAsync(cancellationToken));
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
            Assert.Equal(0, await db.RecommendationRuns.CountAsync(cancellationToken));
        });

    private List<Guid> DrainRecommendationQueue()
    {
        var queue = _factory.Resolve<RecommendationRunQueue>();
        var ids = new List<Guid>();
        while (queue.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }

    private List<Guid> DrainSuggestionQueue()
    {
        var queue = _factory.Resolve<LibraryLinkSuggestionRunQueue>();
        var ids = new List<Guid>();
        while (queue.Reader.TryRead(out var id))
            ids.Add(id);
        return ids;
    }
}
