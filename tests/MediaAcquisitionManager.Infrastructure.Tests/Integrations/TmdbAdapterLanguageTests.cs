using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-46 — the language TMDb is asked in: every catalog call — details, an episode, a collection, a person, a search, a
// find, a discovery page, a genre list and the lookup's calls — in the integration's catalog language, TMDb's own en-US
// when the settings name none; a work's overview still in the language its reader asks for (Vasyl's decision 2); and the
// languages the adapter offers, TMDb's recorded primary translations.
public sealed class TmdbAdapterLanguageTests : IDisposable
{
    private static readonly IntegrationConnectionSettings BritishEnglish = new(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token, "en-GB");
    private static readonly IntegrationConnectionSettings Ukrainian = new(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token, "uk-UA");
    private static readonly IntegrationConnectionSettings NoLanguage = new(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);

    private readonly IntegrationAdapterHarness _harness = new();

    public TmdbAdapterLanguageTests()
    {
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(
            MediaWorkKind.Movie,
            679,
            "Aliens",
            "Aliens",
            "1986-07-18",
            ImdbId: "tt0090605",
            CollectionId: 8091,
            DirectorIds: [2710],
            TitlesByLanguage: new Dictionary<string, string> { ["uk-UA"] = "Чужі", ["en-GB"] = "Aliens" }));
        tmdb.AddCollection(8091, "Alien Collection", 679);
        tmdb.AddPerson(2710, "James Cameron");
        tmdb.AddEpisode(new FakeTmdbEpisode(1399, 1, 1, "Winter Is Coming", "tt1480055"));
        tmdb.AddSearch(MediaWorkKind.Movie, "Aliens", 1986, 679);
        tmdb.AddFind("tt0090605", MediaWorkKind.Movie, 679);
        tmdb.AddDiscover(MediaWorkKind.Movie, 1, 679);
    }

    public static TheoryData<string> Calls => new()
    {
        "details", "episode", "collection", "person", "search", "find", "discover", "genres", "lookup by id", "lookup by imdb id", "lookup by title",
    };

    private IMetadataCatalog Catalog => _harness.Adapter<IMetadataCatalog>(TmdbAdapter.TypeId);

    private IMetadataResolver Resolver => _harness.Adapter<IMetadataResolver>(TmdbAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Every_catalog_call_asks_in_the_settings_catalog_language(string call)
    {
        //Arrange

        //Act
        var status = await CallAsync(call, BritishEnglish, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, status);
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(["en-GB"], LanguagesOf(request.Uri));
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Every_catalog_call_without_a_catalog_language_asks_in_tmdbs_own_en_us(string call)
    {
        //Arrange

        //Act
        var status = await CallAsync(call, NoLanguage, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, status);
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal(["en-US"], LanguagesOf(request.Uri));
    }

    [Fact]
    public async Task GetWork_answers_the_title_tmdb_gives_in_the_language_asked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var english = await Catalog.GetWorkAsync(BritishEnglish, MediaWorkKind.Movie, "679", cancellationToken);
        var ukrainian = await Catalog.GetWorkAsync(Ukrainian, MediaWorkKind.Movie, "679", cancellationToken);

        //Assert
        Assert.Equal(("Aliens", "Aliens"), (english.Value!.Summary.Title, english.Value.Summary.OriginalTitle));
        Assert.Equal(("Чужі", "Aliens"), (ukrainian.Value!.Summary.Title, ukrainian.Value.Summary.OriginalTitle));
    }

    [Fact]
    public async Task GetOverview_asks_in_the_readers_language_whatever_the_catalogs()
    {
        //Arrange

        //Act
        var result = await Catalog.GetOverviewAsync(BritishEnglish, MediaWorkKind.Movie, "679", "uk-UA", TestContext.Current.CancellationToken);

        //Assert
        // Vasyl's decision 2: a card's Description is the reader's, the catalog's language is the titles'.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(["uk-UA"], LanguagesOf(Assert.Single(_harness.TmdbHttp.Requests).Uri));
    }

    [Fact]
    public async Task A_catalog_language_edited_into_the_row_by_hand_adds_no_parameter_of_its_own()
    {
        //Arrange
        var settings = BritishEnglish with { CatalogLanguage = "en-GB&page=2" };

        //Act
        await Catalog.GetGenresAsync(settings, MediaWorkKind.Movie, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/genre/movie/list?language=en-GB%26page%3D2", request.Uri.AbsoluteUri);
    }

    [Fact]
    public void Languages_are_tmdbs_recorded_primary_translations_in_its_order()
    {
        //Arrange
        var recorded = JsonSerializer.Deserialize<string[]>(TmdbSamples.ConfigurationPrimaryTranslations())!;

        //Act
        var languages = Catalog.Languages;

        //Assert
        Assert.Equal(recorded, languages);
        Assert.Contains("uk-UA", languages);
        Assert.Contains(TmdbAdapter.DefaultLanguage, languages);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Task<IntegrationCallStatus> CallAsync(string call, IntegrationConnectionSettings settings, CancellationToken cancellationToken) => call switch
    {
        "details" => StatusOf(Catalog.GetWorkAsync(settings, MediaWorkKind.Movie, "679", cancellationToken)),
        "episode" => StatusOf(Catalog.GetEpisodeAsync(settings, "1399", 1, 1, cancellationToken)),
        "collection" => StatusOf(Catalog.GetCollectionAsync(settings, "8091", cancellationToken)),
        "person" => StatusOf(Catalog.GetPersonAsync(settings, "2710", cancellationToken)),
        "search" => StatusOf(Catalog.SearchAsync(settings, MediaWorkKind.Movie, "Aliens", 1986, cancellationToken)),
        "find" => StatusOf(Catalog.FindByImdbIdAsync(settings, "tt0090605", cancellationToken)),
        "discover" => StatusOf(Catalog.DiscoverAsync(settings, MediaWorkKind.Movie, [16], [], 1, cancellationToken)),
        "genres" => StatusOf(Catalog.GetGenresAsync(settings, MediaWorkKind.Movie, cancellationToken)),
        "lookup by id" => StatusOf(Resolver.LookupAsync(
            settings, new MetadataQuery(MediaWorkKind.Movie, [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], null, null), cancellationToken)),
        "lookup by imdb id" => StatusOf(Resolver.LookupAsync(
            settings, new MetadataQuery(MediaWorkKind.Movie, [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0090605")], null, null), cancellationToken)),
        "lookup by title" => StatusOf(Resolver.LookupAsync(settings, new MetadataQuery(MediaWorkKind.Movie, [], "Aliens", 1986), cancellationToken)),
        _ => throw new ArgumentOutOfRangeException(nameof(call), call, null),
    };

    private static async Task<IntegrationCallStatus> StatusOf<T>(Task<IntegrationCallResult<T>> call) where T : class => (await call).Status;

    // Every language parameter of the request, decoded: exactly one is the rule.
    private static string[] LanguagesOf(Uri uri) =>
        [.. uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == "language")
            .Select(pair => Uri.UnescapeDataString(pair[1]))];
}
