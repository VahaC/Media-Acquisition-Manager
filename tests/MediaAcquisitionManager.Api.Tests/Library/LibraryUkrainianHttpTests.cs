using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-47 WP14 (ADR 0042, design §8.6) — the Library page in Ukrainian: a sync's and a scan's stored reasons are rendered
// in the reader's language when they are read — Jellyfin's own Ukrainian labels inside a refusal, the counted reason a
// changing library gives in the noun's own form (21 фільм, 3 фільми), a reason an older build stored in English converted
// at start, a scan's problem with its path in «…» —, a refused sort quotes its value, and search_library counts the works
// it found. The same request without the header answers English. Each answer spelled here, never read from the files.
public sealed class LibraryUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string SourcesUrl = "/api/library/sources";
    private const string ScansUrl = "/api/library/scans";
    private const string WorksUrl = "/api/library/works";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public LibraryUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sources_asked_in_ukrainian_read_a_failed_syncs_reason_with_jellyfins_own_ukrainian_labels()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new LibrarySyncRunSeeder(_factory.Scope).Create(_livingRoom, LibrarySyncStatus.Failed, _owner.User, error: Message.Of("jellyfin.key-refused"));

        //Act
        var ukrainian = await LatestSyncErrorAsync("uk", cancellationToken);
        var english = await LatestSyncErrorAsync(null, cancellationToken);

        //Assert
        Assert.Equal(
            "Сервіс Jellyfin відхилив ключ API. Створіть ключ у Jellyfin на сторінці «Панель керування» → «API-ключі» і введіть його тут.",
            ukrainian);
        Assert.Equal("Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.", english);
    }

    [Theory]
    [InlineData(21, 22, "На початку синхронізації сервіс Jellyfin повідомляв про 21 фільм, а наприкінці — про 22, тож медіатека змінювалася під час читання. У застосунку нічого не змінено; синхронізуйте ще раз, коли сервіс Jellyfin завершить сканування медіатеки.")]
    [InlineData(3, 2, "На початку синхронізації сервіс Jellyfin повідомляв про 3 фільми, а наприкінці — про 2, тож медіатека змінювалася під час читання. У застосунку нічого не змінено; синхронізуйте ще раз, коли сервіс Jellyfin завершить сканування медіатеки.")]
    public async Task Sources_asked_in_ukrainian_count_the_movies_a_changing_library_listed_in_their_own_form(int count, int last, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var reason = Message.Of("library.sync.total-changed-movies", ("service", "Jellyfin"), ("count", count), ("last", last));
        await new LibrarySyncRunSeeder(_factory.Scope).Create(_livingRoom, LibrarySyncStatus.Failed, _owner.User, error: reason);

        //Act
        var error = await LatestSyncErrorAsync("uk", cancellationToken);

        //Assert
        Assert.Equal(expected, error);
    }

    [Fact]
    public async Task Sources_asked_in_ukrainian_read_a_reason_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = await new LibrarySyncRunSeeder(_factory.Scope).Create(_livingRoom, LibrarySyncStatus.Failed, _owner.User, error: Message.Of("library.sync.app-error"));
        await _factory.ConvertStoredEnglishAsync("LibrarySyncRuns", "Error", run.Id, "The app restarted before this sync finished. Start it again.");

        //Act
        var error = await LatestSyncErrorAsync("uk", cancellationToken);

        //Assert
        Assert.Equal("Застосунок перезапустився до завершення цієї синхронізації. Запустіть її знову.", error);
    }

    [Fact]
    public async Task Scan_asked_in_ukrainian_reads_its_problem_with_the_path_in_guillemets()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var run = await new FolderScanRunSeeder(_factory.Scope).Create(
            FolderScanStatus.Succeeded, _owner.User, problemCount: 1, problem: Message.Of("files.inventory.empty-root", ("path", "/media")));

        //Act
        var response = await _factory.GetHttpRequest($"{ScansUrl}/{run.Id}", _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<FolderScanRunDto>(MediaAcquisitionManagerApiFactory.PlainJsonOptions, cancellationToken);
        Assert.Equal(
            "Тека «/media» порожня; якщо це точка монтування, її, можливо, зараз не змонтовано, тож файли, знайдені в ній раніше, збережено.",
            scan!.Problem);
    }

    [Fact]
    public async Task Works_with_a_sort_it_lacks_asked_in_ukrainian_is_400_quoting_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{WorksUrl}?sort=Bogus", _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «Bogus» не є способом сортування. Вкажіть Title, TitleDescending, Newest, Oldest, Largest або Added.","code":"library.list.sort-unknown"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("dune", "За запитом «dune» знайдено 2 твори; на сторінці 1 показано 2.")]
    [InlineData("tt0816692", "За запитом «tt0816692» знайдено 1 твір; на сторінці 1 показано 1.")]
    public async Task SearchLibrary_on_a_connection_asking_for_ukrainian_counts_the_works_it_found_in_their_own_form(string text, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var works = new MediaWorkSeeder(_factory.Scope);
        await works.CreateMovie(_livingRoom, "Інтерстеллар", 2014, "157336", "tt0816692", "Interstellar", [40_000_000_000]);
        await works.CreateMovie(_livingRoom, "Дюна", 2021, "438631", "tt1160419", "Dune", [20_000_000_000]);
        await works.CreateMovie(_livingRoom, "Дюна: Частина друга", 2024, "693134", "tt15239678", "Dune: Part Two", [30_000_000_000]);
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("search_library", new Dictionary<string, object?> { ["text"] = text }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(expected, result.ReadRawText());
    }

    private async Task<string?> LatestSyncErrorAsync(string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(SourcesUrl, _owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        var sources = await response.Content.ReadFromJsonAsync<LibrarySourceListResponse>(options, cancellationToken);
        return Assert.Single(sources!.Items).LatestRun!.Error;
    }
}
