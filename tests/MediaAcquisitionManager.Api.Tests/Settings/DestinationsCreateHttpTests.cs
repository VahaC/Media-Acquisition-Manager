using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-11 — adding a destination: what is stored, its audit row, every rule in its order, and decision 3 —
// nothing is saved until the client confirms the category: 409 when it can't be asked or hasn't got it,
// 502 with the client's sentence when it was asked and couldn't answer. The default's rules are
// DestinationsDefaultHttpTests.
public sealed class DestinationsCreateHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/destinations";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    // Spelled out rather than read from the product code: these are what the Add destination dialog shows.
    private const string NameMissingError = "A destination needs a name.";
    private const string ClientMissingError = "Pick the download client this destination belongs to.";
    private const string KeyMissingError = "Pick the category or folder of the download client this destination saves to.";
    private const string ClientGoneError = "The download client you picked no longer exists. Reload the page and pick one again.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IDestinationMappingSeeder _destinations = null!;
    private AuthorizedUser _owner = null!;
    private FakeQBittorrentServer _qbittorrent = null!;

    public DestinationsCreateHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public static TheoryData<string, string> ClientsThatCouldNotAnswer => new()
    {
        { "unreachable", "Couldn't reach qBittorrent at http://qbittorrent.test:8080: nothing accepted the connection. Check the address and port, and that qBittorrent is running." },
        { "refused sign-in", "qBittorrent refused the user name or password." },
        { "old version", "This is qBittorrent v4.3.9; the app works with qBittorrent 4.4.1 or later. Update qBittorrent, then test again." },
        { "failing", "qBittorrent answered 500 Internal Server Error. Its own log says why." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _destinations = new DestinationMappingSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_is_201_with_the_destination_its_check_and_the_location_that_reads_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await SyncSampleLibrariesAsync();
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equivalent(
            new DestinationDto(
                body!.Id,
                "Movies",
                // The first destination: the default without being asked (decision 2).
                IsDefault: true,
                client.Id,
                "qBittorrent",
                "Movies",
                new DestinationCheckDto(DestinationPathStatus.InLibrary, "/downloads/movies", "/media/movies", ["Фільми"], null),
                now,
                // MAM-30 — qBittorrent's destinations are its categories.
                DownloadDestinationKind.Category),
            body,
            strict: true);
        Assert.Equal($"/api/settings/destinations/{body.Id}", response.Headers.Location?.AbsolutePath);
        var read = await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, _owner.AccessToken);
        Assert.Equivalent(body, await read.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken), strict: true);
    }

    [Fact]
    public async Task Create_persists_the_name_trimmed_and_the_category_exactly_as_the_client_spells_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("  Серіали 4K  ", client.Id, "TV Shows"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DestinationMappings.SingleAsync(cancellationToken);
            Assert.Equal("Серіали 4K", row.Name);
            Assert.Equal("СЕРІАЛИ 4K", row.NormalizedName);
            Assert.Equal(client.Id, row.DownloadClientConnectionId);
            // With its space: what a download sends, never trimmed or normalised.
            Assert.Equal("TV Shows", row.ClientDestinationKey);
            Assert.True(row.IsDefault);
            Assert.Equal(now, row.CreatedUtc);
        });
    }

    [Fact]
    public async Task Create_records_a_destination_created_audit_row_with_the_owner_and_the_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<DestinationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var audit = await db.AuditEvents.SingleAsync(cancellationToken);
            Assert.Equal(AuditAction.DestinationCreated, audit.Action);
            Assert.Equal(_owner.User.Id, audit.ActorUserId);
            Assert.Equal("owner", audit.ActorUserName);
            Assert.Equal(AuditTargetKind.Destination, audit.TargetKind);
            Assert.Equal(body!.Id, audit.TargetId);
            Assert.Equal("Movies", audit.TargetName);
            Assert.Equal(now, audit.CreatedUtc);
        });
    }

    [Fact]
    public async Task Create_asks_the_client_once_with_the_sign_in_and_reads_only()
    {
        //Arrange
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        // Once for both the confirmation and the check the answer carries; and nothing sent that changes
        // anything in qBittorrent — no category created, no save path.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            ["POST /api/v2/auth/login", "GET /api/v2/app/version", "GET /api/v2/torrents/categories"],
            _factory.QBittorrentHttp.Requests.Select(request => $"{request.Method} {request.Uri.AbsolutePath}"));
    }

    // MAM-11 — a blank or missing field reaches the rules and gets their sentence, which the Add destination
    // dialog shows as is; a framework validation body has no "error" for it to show.
    [Theory]
    [InlineData("empty name", NameMissingError)]
    [InlineData("whitespace-only name", NameMissingError)]
    [InlineData("missing name", NameMissingError)]
    [InlineData("missing client", ClientMissingError)]
    [InlineData("empty category", KeyMissingError)]
    [InlineData("whitespace-only category", KeyMissingError)]
    [InlineData("missing category", KeyMissingError)]
    [InlineData("empty body", NameMissingError)]
    public async Task Create_with_a_blank_or_missing_field_is_400_with_the_rule_and_asks_no_client(string blank, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        object body = blank switch
        {
            "empty name" => new CreateDestinationRequest("", client.Id, "Movies"),
            "whitespace-only name" => new CreateDestinationRequest("   ", client.Id, "Movies"),
            "missing name" => new { downloadClientId = client.Id, clientDestinationKey = "Movies" },
            "missing client" => new { name = "Movies", clientDestinationKey = "Movies" },
            "empty category" => new CreateDestinationRequest("Movies", client.Id, ""),
            "whitespace-only category" => new CreateDestinationRequest("Movies", client.Id, "   "),
            "missing category" => new { name = "Movies", downloadClientId = client.Id },
            "empty body" => new { },
            _ => throw new ArgumentOutOfRangeException(nameof(blank), blank, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, body);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData("65 characters", "A destination's name can be at most 64 characters.")]
    [InlineData("a line break", "A destination's name can't contain line breaks or other control characters.")]
    public async Task Create_with_an_invalid_name_is_400_with_the_rule(string invalid, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var name = invalid switch
        {
            "65 characters" => new string('n', 65),
            "a line break" => "Movies\n4K",
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest(name, client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [InlineData("257 characters", "A category or folder can be at most 256 characters.")]
    [InlineData("a tab", "A category or folder can't contain line breaks or other control characters.")]
    public async Task Create_with_a_category_name_the_app_can_not_keep_is_400_with_the_rule(string invalid, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        var key = invalid switch
        {
            "257 characters" => new string('k', 257),
            "a tab" => "Movies\t4K",
            _ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, key));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [InlineData("no integration")]
    [InlineData("a media library")]
    public async Task Create_for_an_id_that_is_not_a_download_client_is_404_with_the_sentence(string target)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var id = target == "no integration" ? Guid.NewGuid() : (await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey)).Id;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", id, "Movies"));

        //Assert
        // An id in the body, not the addressed row: the dialog says what happened.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ClientGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("MOVIES")]
    [InlineData("  movies  ")]
    public async Task Create_with_a_name_the_client_has_in_any_letter_case_is_409_and_asks_it_nothing(string name)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Movies", "Movies");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest(name, client.Id, "Cartoons"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal($"“qBittorrent” already has a destination named “{name.Trim()}”.", await response.ReadErrorAsync(cancellationToken));
        await AssertDestinationCountAsync(1, cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_with_a_name_only_another_client_has_is_201()
    {
        //Arrange
        var other = await _integrations.Create(
            "qbittorrent", "Other", "http://127.0.0.1:9", FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword);
        await _destinations.Create(other, "Movies", "Movies");
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        // Unique per client: "Movies" on two clients are two destinations.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await AssertDestinationCountAsync(2, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Create_for_a_switched_off_client_is_409_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync(isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“qBittorrent” is switched off. Switch it on under Integrations to use it for destinations.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Fact]
    public async Task Create_for_a_type_this_version_can_not_send_downloads_to_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await _integrations.Create("retired-client", "Old client", kind: IntegrationKind.DownloadClient);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("This version of the app can't send downloads to “retired-client”.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_for_a_client_whose_stored_secret_can_not_be_read_is_409_and_asks_it_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await _integrations.CreateWithUnreadableApiKey("qbittorrent", "qBittorrent");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "The stored API key of “qBittorrent” can't be read with this installation's encryption key. Enter the API key again and save.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
    }

    [Theory]
    [InlineData("Movies 4K")]
    // The client's name exactly: not in another letter case, not with a space around it.
    [InlineData("movies")]
    [InlineData(" Movies")]
    public async Task Create_of_a_category_the_client_has_not_got_is_409_and_writes_nothing(string key)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, key));

        //Assert
        // qBittorrent would create an unknown category on the first download, with a folder nobody chose.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            $"“qBittorrent” has no category “{key}”. Create it in the client first, or pick one it has.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_category_the_client_removed_since_the_dialog_listed_it_is_409()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _factory.GetHttpRequest($"{BaseUrl}/client-destinations/{client.Id}", _owner.AccessToken);
        _qbittorrent.Categories.Remove("Movies");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        // Asked again at the save, not trusted from the list the dialog showed.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "“qBittorrent” has no category “Movies”. Create it in the client first, or pick one it has.",
            await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Theory]
    [MemberData(nameof(ClientsThatCouldNotAnswer))]
    public async Task Create_when_the_client_could_not_answer_is_502_with_its_sentence_and_writes_nothing(string failure, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        switch (failure)
        {
            case "unreachable":
                _qbittorrent.Unreachable = true;
                break;
            case "refused sign-in":
                _qbittorrent.Password = "changed-in-qbittorrent";
                break;
            case "old version":
                _qbittorrent.Version = "v4.3.9";
                break;
            case "failing":
                _qbittorrent.Intercept = request => request.RequestUri!.AbsolutePath.EndsWith("/categories", StringComparison.Ordinal)
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : null;
                break;
        }

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_twice_at_once_with_one_name_for_one_client_creates_one_destination()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        HttpResponseMessage? competing = null;
        // Deterministic overlap: this create passed every check — the name free, and no destination yet,
        // so it takes the default — and is about to begin its save's transaction when the other one is
        // sent and saved whole.
        _factory.TransactionHook.BeforeNextTransaction(
            context => context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Added),
            async _ => competing = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Twin", client.Id, "Movies")));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("TWIN", client.Id, "Cartoons"));

        //Assert
        // The unique index lets only one of them commit, and the loser is a 409, not a 500.
        Assert.Equal(HttpStatusCode.Created, competing?.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“qBittorrent” already has a destination named “TWIN”.", await response.ReadErrorAsync(cancellationToken));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DestinationMappings.SingleAsync(cancellationToken);
            Assert.Equal("Twin", row.Name);
            // The one that was saved is the first destination, so the default; the loser cleared it inside
            // its transaction before its insert failed, and that rolled back with the rest of its save.
            Assert.True(row.IsDefault);
            Assert.Equal(1, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_when_another_request_saves_the_name_first_is_409_and_keeps_only_that_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Cartoons", "Cartoons", isDefault: true);
        DestinationMappingEntity? competing = null;
        // Deterministic race: this create passed the "name is free" check and the client confirmed the
        // category, and the other request's insert lands just before this one's — through a context of
        // its own, like another request.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Added),
            async _ =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                competing = await new DestinationMappingSeeder(scope.ServiceProvider).Create(client, "MOVIES", "Movies");
            });

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        // The unique index settles it: the loser is a 409, not a 500, and leaves no audit row behind.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("“qBittorrent” already has a destination named “Movies”.", await response.ReadErrorAsync(cancellationToken));
        Assert.NotNull(competing);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(competing.Id, (await db.DestinationMappings.SingleAsync(row => row.NormalizedName == "MOVIES", cancellationToken)).Id);
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_when_the_client_is_removed_before_the_save_is_404_with_the_sentence_and_writes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = await CreateClientAsync();
        await _destinations.Create(client, "Cartoons", "Cartoons", isDefault: true);
        // The client confirmed the category, then another request removed the integration — its
        // destinations with it — just before this save.
        _factory.SaveChangesHook.BeforeNextSave(
            context => context.ChangeTracker.Entries<DestinationMappingEntity>().Any(entry => entry.State == EntityState.Added),
            async token =>
            {
                await using var scope = _factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().IntegrationConnections
                    .Where(integration => integration.Id == client.Id)
                    .ExecuteDeleteAsync(token);
            });

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateDestinationRequest("Movies", client.Id, "Movies"));

        //Assert
        // The foreign key settles it: the client is gone, which the dialog says, rather than a 500.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ClientGoneError, await response.ReadErrorAsync(cancellationToken));
        await AssertNothingWrittenAsync(cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // A qBittorrent at the fake server's address that signs in with the server's credentials and maps its
    // /downloads to the app's /media, as ADR 0003's reference installation does.
    private Task<IntegrationConnectionEntity> CreateClientAsync(bool isEnabled = true) =>
        _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            isEnabled: isEnabled,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);

    // The libraries of VendorSamples/Jellyfin/virtual-folders.json, stored by a real sync.
    private async Task SyncSampleLibrariesAsync()
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        new FakeJellyfinServer { ApiKey = JellyfinApiKey }.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin);
    }

    private Task AssertNothingWrittenAsync(CancellationToken cancellationToken) => _factory.WithDbAsync(async db =>
    {
        Assert.Equal(0, await db.DestinationMappings.CountAsync(cancellationToken));
        Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
    });

    private Task AssertDestinationCountAsync(int expected, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db => Assert.Equal(expected, await db.DestinationMappings.CountAsync(cancellationToken)));
}
