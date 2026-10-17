using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — the lookup a match runs (IMetadataResolver): by the work's own TMDb id, by its IMDb id, or by
// its title and year, each candidate with the evidence it matched by; the real answers where TMDb's
// samples show the case, the fake TMDb where a search must list chosen works.
public sealed class TmdbAdapterLookupTests : IDisposable
{
    private const string BaseUrl = "https://api.themoviedb.test";
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJsb29rdXAifQ.c2lnbmF0dXJlLW9mLWxvb2t1cA";

    // MAM-46 — the language an upgraded installation's TMDb keeps, which every exact URL below was recorded with.
    private const string CatalogLanguage = "uk-UA";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, Token, CatalogLanguage);

    private static readonly IntegrationConnectionSettings FakeSettings = new(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token, CatalogLanguage);

    /// <summary>Queries no caller should make, by what is wrong with them.</summary>
    private static readonly Dictionary<string, MetadataQuery> MistakenQueries = new(StringComparer.Ordinal)
    {
        ["an episode"] = new MetadataQuery(MediaWorkKind.Episode, [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0071562")], "Alien", null),
        ["a kind nobody knows"] = new MetadataQuery((MediaWorkKind)9, [], "Alien", null),
        ["neither an id nor a title"] = new MetadataQuery(MediaWorkKind.Movie, [], null, 1979),
        ["a title without a letter or a digit"] = new MetadataQuery(MediaWorkKind.Movie, [], " — ", 1979),
        ["a TVDB id alone"] = new MetadataQuery(MediaWorkKind.Series, [new MediaIdentifier(MediaIdentifierScheme.Tvdb, "341164")], null, null),
        ["a malformed TMDb id"] = new MetadataQuery(MediaWorkKind.Movie, [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "../679")], null, null),
        ["a malformed IMDb id"] = new MetadataQuery(MediaWorkKind.Movie, [new MediaIdentifier(MediaIdentifierScheme.Imdb, "0090605")], null, null),
        ["a year no work has"] = new MetadataQuery(MediaWorkKind.Movie, [], "Alien", 0),
    };

    private readonly IntegrationAdapterHarness _harness = new();

    private IMetadataResolver Resolver => _harness.Adapter<IMetadataResolver>(TmdbAdapter.TypeId);

    public static TheoryData<string> Mistakes => new(MistakenQueries.Keys);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Capabilities_are_imdb_tmdb_and_tmdb_tv_ids_and_a_title()
    {
        //Arrange

        //Act
        var capabilities = Resolver.Capabilities;

        //Assert
        Assert.Equal([MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.TmdbTv], capabilities.IdentifierSchemes);
        Assert.True(capabilities.SearchesByTitle);
    }

    [Fact]
    public async Task Lookup_by_an_imdb_id_finds_the_film_tmdb_files_under_it()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.FindImdbFile, TmdbSamples.MediaType);
        var query = new MetadataQuery(MediaWorkKind.Movie, [new MediaIdentifier(MediaIdentifierScheme.Imdb, TmdbSamples.FindImdbId)], "Хрещений батько 2", 1974);

        //Act
        var result = await Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/find/tt0071562?external_source=imdb_id&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal([$"Bearer {Token}"], request.HeaderValues("Authorization"));
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal("240", candidate.SourceItemId);
        Assert.Equal(
            [new ReportedTitle("Хрещений батько 2", MediaTitleKind.Display, 1974), new ReportedTitle("The Godfather Part II", MediaTitleKind.Original, 1974)],
            candidate.Titles);
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "240"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0071562")],
            candidate.Identifiers);
        Assert.Equal(MatchEvidence.Identifier, candidate.Evidence);
    }

    [Fact]
    public async Task Lookup_of_a_show_by_an_imdb_id_leaves_out_the_films_filed_under_it()
    {
        //Arrange
        // The sample files a film under the id and no show.
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.FindImdbFile, TmdbSamples.MediaType);
        var query = new MetadataQuery(MediaWorkKind.Series, [new MediaIdentifier(MediaIdentifierScheme.Imdb, TmdbSamples.FindImdbId)], null, null);

        //Act
        var result = await Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Lookup_by_a_tmdb_id_reads_the_works_details_without_credits_or_recommendations()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.MovieDetailsFile, TmdbSamples.MediaType);
        // Both ids: the work's own TMDb id comes first, the IMDb id is not asked for.
        var query = new MetadataQuery(
            MediaWorkKind.Movie,
            [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt2015381"), new MediaIdentifier(MediaIdentifierScheme.Tmdb, TmdbSamples.MovieDetailsId)],
            null,
            null);

        //Act
        var result = await Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{BaseUrl}/3/movie/118340?language=uk-UA", request.Uri.AbsoluteUri);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal("118340", candidate.SourceItemId);
        Assert.Equal(
            [new ReportedTitle("Вартові галактики", MediaTitleKind.Display, 2014), new ReportedTitle("Guardians of the Galaxy", MediaTitleKind.Original, 2014)],
            candidate.Titles);
        // The IMDb id the details give.
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "118340"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt2015381")],
            candidate.Identifiers);
        Assert.Equal(MatchEvidence.Identifier, candidate.Evidence);
    }

    [Fact]
    public async Task Lookup_of_a_show_by_its_tmdb_tv_id_reads_the_shows_details()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.TvDetailsFile, TmdbSamples.MediaType);
        var query = new MetadataQuery(MediaWorkKind.Series, [new MediaIdentifier(MediaIdentifierScheme.TmdbTv, TmdbSamples.TvDetailsId)], null, null);

        //Act
        var result = await Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal($"{BaseUrl}/3/tv/73586?language=uk-UA", Assert.Single(_harness.TmdbHttp.Requests).Uri.AbsoluteUri);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal(
            [new ReportedTitle("Єллоустоун", MediaTitleKind.Display, 2018), new ReportedTitle("Yellowstone", MediaTitleKind.Original, 2018)],
            candidate.Titles);
        // A show's details carry no IMDb id.
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "73586")], candidate.Identifiers);
        Assert.Equal(MatchEvidence.Identifier, candidate.Evidence);
    }

    [Fact]
    public async Task Lookup_of_a_film_ignores_a_tmdb_tv_id_and_looks_it_up_by_its_imdb_id()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Sample(TmdbSamples.FindImdbFile, TmdbSamples.MediaType);
        // TMDb numbers films and shows apart (ADR 0017): a show's id is not this film's.
        var query = new MetadataQuery(
            MediaWorkKind.Movie,
            [new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "240"), new MediaIdentifier(MediaIdentifierScheme.Imdb, TmdbSamples.FindImdbId)],
            null,
            null);

        //Act
        var result = await Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        Assert.StartsWith($"{BaseUrl}/3/find/", Assert.Single(_harness.TmdbHttp.Requests).Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Lookup_by_a_tmdb_id_tmdb_does_not_have_finds_no_candidate()
    {
        //Arrange
        FakeTmdbServer.Arm(_harness.TmdbHttp);
        var query = new MetadataQuery(MediaWorkKind.Movie, [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "999999")], null, null);

        //Act
        var result = await Resolver.LookupAsync(FakeSettings, query, TestContext.Current.CancellationToken);

        //Assert
        // An answer, not a failure: nothing found, as a search with no results.
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Lookup_by_a_title_and_year_keeps_the_titles_that_match_with_their_evidence()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 679, "Чужі", "Aliens", "1986-07-18"));
        // The same title, another year: matched by its title only.
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 70001, "Aliens!", "Aliens!", "1990-01-01"));
        // TMDb's search lists look-alikes too: never a candidate.
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 348, "Чужий", "Alien", "1986-05-25"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 70002, "Aliens in the Attic", "Aliens in the Attic", "1986-07-31"));
        // Not released yet: its title matches, its year can't.
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 70003, "Чужі", "Aliens", null));
        tmdb.AddSearch(MediaWorkKind.Movie, "Aliens", 1986, 70002, 348, 679, 70001, 70003);
        var query = new MetadataQuery(MediaWorkKind.Movie, [], "Aliens", 1986);

        //Act
        var result = await Resolver.LookupAsync(FakeSettings, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TmdbHttp.Requests);
        Assert.Equal($"{FakeTmdbServer.BaseUrl}/3/search/movie?query=Aliens&year=1986&language=uk-UA", request.Uri.AbsoluteUri);
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        // In TMDb's order; it never picks one.
        Assert.Equal(
            [("679", MatchEvidence.TitleAndYear), ("70001", MatchEvidence.TitleOnly), ("70003", MatchEvidence.TitleOnly)],
            result.Value!.Select(candidate => (candidate.SourceItemId, candidate.Evidence)));
        var aliens = result.Value![0];
        Assert.Equal(
            [new ReportedTitle("Чужі", MediaTitleKind.Display, 1986), new ReportedTitle("Aliens", MediaTitleKind.Original, 1986)],
            aliens.Titles);
        // A search gives no IMDb id.
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], aliens.Identifiers);
        // The same title twice is given once.
        Assert.Equal([new ReportedTitle("Aliens!", MediaTitleKind.Display, 1990)], result.Value[1].Titles);
        Assert.Equal([new ReportedTitle("Чужі", MediaTitleKind.Display, null), new ReportedTitle("Aliens", MediaTitleKind.Original, null)], result.Value[2].Titles);
    }

    [Fact]
    public async Task Lookup_by_a_title_without_a_year_matches_by_title_only_through_either_title()
    {
        //Arrange
        var tmdb = FakeTmdbServer.Arm(_harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 73586, "Єллоустоун", "Yellowstone", "2018-06-20"));
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Series, 157744, "Єллоустоун: 1923", "1923", "2022-12-18"));
        tmdb.AddSearch(MediaWorkKind.Series, "єллоустоун", null, 73586, 157744);
        var query = new MetadataQuery(MediaWorkKind.Series, [new MediaIdentifier(MediaIdentifierScheme.Tvdb, "341164")], "ЄЛЛОУСТОУН", null);

        //Act
        var result = await Resolver.LookupAsync(FakeSettings, query, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            $"{FakeTmdbServer.BaseUrl}/3/search/tv?query={Uri.EscapeDataString("ЄЛЛОУСТОУН")}&language=uk-UA",
            Assert.Single(_harness.TmdbHttp.Requests).Uri.AbsoluteUri);
        var candidate = Assert.Single(result.Value!);
        Assert.Equal(("73586", MatchEvidence.TitleOnly), (candidate.SourceItemId, candidate.Evidence));
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "73586")], candidate.Identifiers);
    }

    [Fact]
    public async Task Lookup_by_a_title_tmdb_answers_with_a_failure_passes_the_failure_on()
    {
        //Arrange
        _harness.TmdbHttp.Handler = (_, _) => VendorResponses.Recorded(TmdbSamples.WrongKeyResponseFile);
        var query = new MetadataQuery(MediaWorkKind.Movie, [], "Aliens", 1986);

        //Act
        var result = await Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Null(result.Value);
        Assert.DoesNotContain(Token, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task Lookup_refuses_a_query_no_caller_should_make_without_sending_anything(string mistake)
    {
        //Arrange
        var query = MistakenQueries[mistake];

        //Act
        var exception = await Record.ExceptionAsync(() => Resolver.LookupAsync(Settings, query, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.TmdbHttp.Requests);
    }
}
