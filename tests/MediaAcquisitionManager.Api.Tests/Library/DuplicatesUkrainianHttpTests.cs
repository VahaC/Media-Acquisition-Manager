using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-47 WP14 (ADR 0042, design §8.6) — the Duplicates page and its Removed copies in Ukrainian: a removal's stored
// reason is rendered in the reader's language when the list is read — its tally nested and counted in the noun's own
// form (1 з 3 імен перебуває, 2 з 3 імен перебувають), a reason an older build stored in English converted at start —, a
// copy's path problem quotes the path in «…», a refused class quotes its value, and list_duplicates counts the groups.
// The same request without the header answers English. Each answer spelled here, never read from the locale files.
public sealed class DuplicatesUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string DuplicatesUrl = "/api/library/duplicates";
    private const string RemovalsUrl = "/api/replacements/removals";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string AlienPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string AlienImdbId = "tt0078748";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private CopyRemovalPlanter _removals = null!;

    public DuplicatesUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _removals = new CopyRemovalPlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(1, "Застосунок зупинився під час повернення цієї копії на місце: 1 з 3 імен перебуває в карантині.")]
    [InlineData(2, "Застосунок зупинився під час повернення цієї копії на місце: 2 з 3 імен перебувають у карантині.")]
    public async Task Removed_copies_asked_in_ukrainian_count_what_the_quarantine_holds_in_their_own_form(int inQuarantine, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var tally = Message.Of(
            "replacements.quarantine.tally.in-quarantine",
            ("count", inQuarantine),
            ("names", Message.Of("replacements.quarantine.tally.names", ("count", 3))));
        await PlantRemovalAsync(Message.Of("replacements.quarantine.cut.restoring-removal-tally", ("tally", tally)));

        //Act
        var reason = await SingleRemovalReasonAsync("uk", cancellationToken);

        //Assert
        Assert.Equal(expected, reason);
    }

    [Fact]
    public async Task Removed_copies_asked_in_ukrainian_read_a_reason_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantRemovalAsync(Message.Of("replacements.quarantine.cut.restoring-removal"));
        await _factory.ConvertStoredEnglishAsync("CopyRemovals", "Reason", planted.Removal.Id, "The app stopped while moving this copy.");

        //Act
        var ukrainian = await SingleRemovalReasonAsync("uk", cancellationToken);
        var english = await SingleRemovalReasonAsync(null, cancellationToken);

        //Assert
        Assert.Equal("Застосунок зупинився під час переміщення цієї копії.", ukrainian);
        Assert.Equal("The app stopped while moving this copy.", english);
    }

    [Fact]
    public async Task List_with_a_class_it_lacks_asked_in_ukrainian_is_400_quoting_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{DuplicatesUrl}?class=Twins", _harness.Owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «Twins» не є видом дублікатів. Вкажіть SameFile, ByteIdentical, SameWork або Possible.","code":"duplicates.list.unknown-class"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task List_asked_in_ukrainian_says_which_copy_no_path_mapping_covers_with_its_path_in_guillemets()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAlienInTwoLibrariesAsync();

        //Act
        var response = await _factory.GetHttpRequest(DuplicatesUrl, _harness.Owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.PlainJsonOptions, cancellationToken);
        var attic = Assert.Single(Assert.Single(list!.Items).Files, file => file.File.ServicePath == "/srv/films/Alien (1979).mkv");
        Assert.Equal("Жодне зіставлення шляхів цієї інтеграції не охоплює шлях «/srv/films/Alien (1979).mkv».", attic.File.PathProblem);
    }

    [Theory]
    [InlineData(false, "Усього 0 груп дублікатів; на сторінці 1 — 0.")]
    [InlineData(true, "Усього 1 група дублікатів; на сторінці 1 — 1.")]
    public async Task ListDuplicates_on_a_connection_asking_for_ukrainian_counts_the_groups_in_their_own_form(bool planted, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        if (planted)
            await PlantAlienInTwoLibrariesAsync();
        var token = await new PersonalAccessTokenSeeder(_factory.Scope).Create(_harness.Owner.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken, language: "uk");

        //Act
        var result = await client.CallToolAsync("list_duplicates", cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(expected, result.ReadRawText());
    }

    private Task<PlantedRemoval> PlantRemovalAsync(Message reason) =>
        _removals.PlantAsync(
            CopyRemovalState.NeedsReview,
            [new PlantedName(ReplacementNameKind.Video, AlienPath, TestMedia.File(inode: 1))],
            reason: reason);

    private async Task<string?> SingleRemovalReasonAsync(string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(RemovalsUrl, _harness.Owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return Assert.Single((await response.Content.ReadFromJsonAsync<CopyRemovalListResponse>(options, cancellationToken))!.Items).Reason;
    }

    // A SameWork group: Alien under one IMDb id in two libraries — the attic's under no prefix its path mapping knows.
    private async Task PlantAlienInTwoLibrariesAsync()
    {
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var livingRoom = await integrations.Create("jellyfin", "Shelf", "http://127.0.0.4:9", apiKey: ApiKey);
        var attic = await integrations.Create("jellyfin", "Attic", "http://127.0.0.3:9", apiKey: ApiKey, pathMappings: [new PathMappingPair("/data", "/media")]);
        await PlantMovieAsync(livingRoom, "/media/films/Alien (1979).mkv");
        await PlantMovieAsync(attic, "/srv/films/Alien (1979).mkv");
    }

    // Alien as a library reported it in an item of its own — its title and year, its IMDb id, one version and one name.
    private async Task PlantMovieAsync(IntegrationConnectionEntity library, string servicePath)
    {
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();
        var works = new MediaWorkDataFactory(db, _factory.Time);
        var itemId = Guid.NewGuid().ToString("N");
        var work = await works.Create();
        await works.AddTitle(work.Id, "Alien", MediaTitleKind.Display, 1979, library.Id, library.Name, itemId);
        await works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, AlienImdbId, library.Id, library.Name, itemId);
        var version = await new MediaVersionDataFactory(db, _factory.Time).Create(work.Id, itemId, sourceConnectionId: library.Id, sourceName: library.Name);
        await new PhysicalFileDataFactory(db, _factory.Time).Create(version.Id, servicePath, sourceConnectionId: library.Id, sourceName: library.Name);
    }
}
