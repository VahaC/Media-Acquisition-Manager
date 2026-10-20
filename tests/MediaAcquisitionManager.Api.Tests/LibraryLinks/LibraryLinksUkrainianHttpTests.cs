using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

// MAM-47 WP14 (ADR 0042, design §8.6) — the links Jellyfin didn't identify, in Ukrainian: a link's stored write error is
// rendered in the reader's language when the list is read — our own button labels in «…» as the web app spells them, the
// type Jellyfin answered kept verbatim, an error an older build stored in English converted at start —, and a refused
// filter quotes its value. The same list without the header answers English. Each answer spelled here, never read from
// the locale files.
public sealed class LibraryLinksUkrainianHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string LinksUrl = "/api/library/tmdb/links";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string ShortId = JellyfinSamples.LandingStriplingId;
    private const string FilmId = JellyfinSamples.LandingStriplingTmdbId;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ILibraryItemLinkSeeder _links = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _livingRoom = null!;
    private Guid _workId;

    public LibraryLinksUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        _links = new LibraryItemLinkSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        // One short in the library, synced, so its link is attached to a work.
        var jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShortId, "Landing Stripling", null, 1962, $"/media/cartoons/Tom & Jerry/{ShortId}.mkv"));
        jellyfin.Arm(_factory.JellyfinHttp);
        _livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await _factory.SyncLibraryAsync(_livingRoom.Id, _owner.AccessToken, cancellationToken);
        _workId = await _factory.WorkOfItemAsync(ShortId, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Not_written_asked_in_ukrainian_reads_a_stopped_batch_with_the_web_apps_button_labels()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantNotWrittenAsync(Message.Of("library-links.write.batch-stopped"));

        //Act
        var ukrainian = await SingleRowAsync("uk", cancellationToken);
        var english = await SingleRowAsync(null, cancellationToken);

        //Assert
        Assert.Equal(
            "Сервіс Jellyfin не підтвердив дві ідентифікації поспіль, тож застосунок зупинив цю групову ідентифікацію, і для цього елемента нічого не надіслано. Перевірте Jellyfin, а потім натисніть «Повторити» або ще раз «Оновити Jellyfin».",
            ukrainian.WriteError);
        Assert.Equal(
            "The app stopped this batch after Jellyfin failed to confirm two identifications in a row, and sent nothing for this item. Check Jellyfin, then Retry, or press Update Jellyfin again.",
            english.WriteError);
    }

    [Fact]
    public async Task Not_written_asked_in_ukrainian_keeps_the_type_jellyfin_answered_verbatim()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantNotWrittenAsync(Message.Of("jellyfin.identify.wrong-type.movie", ("type", VendorText.Clean("BoxSet", 100))));

        //Act
        var row = await SingleRowAsync("uk", cancellationToken);

        //Assert
        Assert.Equal(
            "Під цим ID у Jellyfin зберігається елемент типу «BoxSet», а не фільм, тож застосунок не ідентифікував його. Синхронізуйте медіатеку й прив’яжіть елемент знову.",
            row.WriteError);
    }

    [Fact]
    public async Task Not_written_asked_in_ukrainian_reads_an_error_an_older_build_stored_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var link = await PlantNotWrittenAsync(Message.Of("jellyfin.identify.search-refused"));
        await _factory.ConvertStoredEnglishAsync(
            "LibraryItemLinks", "WriteError", link.Id, "“Living room” is switched off. Switch it on under Integrations, then retry.");

        //Act
        var row = await SingleRowAsync("uk", cancellationToken);

        //Assert
        Assert.Equal("Інтеграцію «Living room» вимкнено. Увімкніть її на сторінці «Інтеграції», а потім повторіть спробу.", row.WriteError);
    }

    [Fact]
    public async Task List_with_a_filter_it_lacks_asked_in_ukrainian_is_400_quoting_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest($"{LinksUrl}?filter=Gone", _owner.AccessToken, language: "uk");

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            """{"error":"Значення «Gone» не є фільтром прив’язок. Вкажіть Unattached, Pending, Conflict, NotWritten або Lost.","code":"library-links.list.filter-invalid"}""",
            await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private Task<LibraryItemLinkEntity> PlantNotWrittenAsync(Message writeError) =>
        _links.Create(
            _livingRoom, ShortId, _workId, MediaIdentifierScheme.Tmdb, FilmId, writeState: LibraryLinkWriteState.NotWritten,
            configure: row => row.WriteError = writeError);

    private async Task<LibraryLinkRowDto> SingleRowAsync(string? language, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{LinksUrl}?filter=NotWritten", _owner.AccessToken, language: language);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // An answer asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return Assert.Single((await response.Content.ReadFromJsonAsync<LibraryLinkListResponse>(options, cancellationToken))!.Items);
    }
}
