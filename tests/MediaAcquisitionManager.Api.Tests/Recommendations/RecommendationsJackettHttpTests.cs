using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-30 — Recommendations with Jackett as the only release source (Vasyl's decision 5), over the real Torznab adapter
// and Jackett's recorded Toloka answers: the availability check counts a film's releases from Jackett's answer as from
// Prowlarr's, a Ukrainian-only film whose titles Jackett's default "Strip Cyrillic Letters" left as "« »" by their
// descriptions; and the feed reader reads Jackett's newest releases, resolving those by the title their descriptions give.
public sealed class RecommendationsJackettHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0001";

    /// <summary>The two Ukrainian-only releases of Jackett's newest movies: "« » (2026) …", described "Потяг «Червона Рута» (2026) …".</summary>
    private static readonly string[] ChervonaRutaReleases = ["https://toloka.to/t700783", "https://toloka.to/t700794"];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private FakeTorznabServer _jackett = null!;

    public RecommendationsJackettHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        await new UserSeeder(_factory.Scope).Create("anna");
        // Jackett is the only release source.
        await new IntegrationConnectionSeeder(_factory.Scope).Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        _jackett = FakeTorznabServer.Jackett(JackettAddress, ApiKey);
        _jackett.SearchXml = TorznabSamples.Feed();
        _jackett.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Check_stores_a_films_availability_from_jacketts_answer_as_from_prowlarrs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _jackett.SearchXml = TorznabSamples.JackettMovieSearch(JackettAddress, ApiKey);
        var film = await _works.Create(MediaWorkKind.Movie, "157336", "Інтерстеллар", new DateOnly(2014, 11, 5), originalTitle: "Interstellar");
        await _candidates.Create(film, RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // The sixteen in the movie categories (the audio release and the trailer aren't asked for), up to 2160p, most
        // with Ukrainian audio, 98 seeders at most.
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, TorznabSamples.JackettItemCount - TorznabSamples.JackettNonMovieItemCount, (int?)2160, true, (int?)98),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
        // Asked at the address Jackett gives, with /api: its caps, then the movie search by the original title and year.
        Assert.Equal(
            [
                $"{JackettAddress}api?t=caps&apikey={ApiKey}",
                $"{JackettAddress}api?t=movie&apikey={ApiKey}&q=Interstellar%202014&cat=2000,2020,2040,2070,5070,5080&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Check_counts_a_ukrainian_only_films_releases_by_their_descriptions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Whatever it is asked, Jackett answers here with its newest 90 films, the two «Потяг «Червона Рута»» among them.
        _jackett.SearchXml = TorznabSamples.JackettLatestMovies(JackettAddress, ApiKey);
        var film = await _works.Create(MediaWorkKind.Movie, "1310001", TorznabSamples.JackettUkrainianOnlyFilmTitle, new DateOnly(2026, 9, 1));
        await _candidates.Create(film, RecommendationTab.Movies);

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        // Their titles "« » (2026) …" name nothing; their descriptions the film's title and year. The best resolution and
        // the audio are read from the titles: "WEB-DL 1080p Ukr" and "WEB-DL-AVC Ukr", 80 and 58 seeders.
        var availability = await AvailabilityAsync(film.Id, cancellationToken);
        Assert.NotNull(availability);
        Assert.Equal(
            (true, 2, (int?)1080, true, (int?)80),
            (availability.Found, availability.ReleaseCount, availability.BestResolution, availability.HasUkrainianAudio, availability.MaxSeeders));
    }

    [Fact]
    public async Task Read_of_jacketts_newest_resolves_a_ukrainian_only_film_by_the_title_its_description_gives()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        var tmdb = FakeTmdbServer.Arm(_factory.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 1310001, TorznabSamples.JackettUkrainianOnlyFilmTitle, TorznabSamples.JackettUkrainianOnlyFilmTitle, "2026-09-01"));
        tmdb.AddSearch(MediaWorkKind.Movie, TorznabSamples.JackettUkrainianOnlyFilmTitle, 2026, 1310001);
        _jackett.SearchXml = TorznabSamples.JackettLatestMovies(JackettAddress, ApiKey);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        List<SourceFeedEntryEntity> entries = [];
        CatalogWorkEntity? work = null;
        await _factory.WithDbAsync(async db =>
        {
            entries = await db.SourceFeedEntries.AsNoTracking().ToListAsync(cancellationToken);
            work = await db.CatalogWorks.AsNoTracking().SingleOrDefaultAsync(row => row.Kind == MediaWorkKind.Movie && row.CatalogId == "1310001", cancellationToken);
        });
        // Every one of Jackett's newest 90 read, the two described "Потяг «Червона Рута» (2026)" resolved to that film.
        Assert.Equal(TorznabSamples.JackettLatestItemCount, entries.Count);
        Assert.NotNull(work);
        Assert.Equal(
            ChervonaRutaReleases,
            entries.Where(entry => entry.CatalogWorkId == work.Id).Select(entry => entry.SourceItemId!).Order(StringComparer.Ordinal));
        Assert.All(entries, entry => Assert.NotNull(entry.ResolvedUtc));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<ReleaseAvailabilityEntity?> AvailabilityAsync(Guid catalogWorkId, CancellationToken cancellationToken)
    {
        ReleaseAvailabilityEntity? availability = null;
        await _factory.WithDbAsync(async db => availability = await db.ReleaseAvailabilities.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CatalogWorkId == catalogWorkId, cancellationToken));
        return availability;
    }
}
