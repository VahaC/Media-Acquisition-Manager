namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-33 — the real TMDb answers of <c>VendorSamples/Tmdb</c>, recorded with <c>language=uk-UA</c>, as
/// text, and the facts the tests read out of them.
/// </summary>
/// <remarks>
/// A test answers with a sample through <c>VendorResponses.Sample(file, MediaType)</c>, or parses a fresh
/// copy (<c>JsonNode.Parse(MovieDetails())</c>) to change what it needs. The recorded 401 is read by
/// <c>VendorResponses.Recorded(WrongKeyResponseFile)</c>.
/// </remarks>
public static class TmdbSamples
{
    /// <summary>What TMDb answers JSON as.</summary>
    public const string MediaType = "application/json;charset=utf-8";

    /// <summary><c>GET /3/authentication</c> with a token TMDb takes: <c>{"success":true}</c>.</summary>
    public const string AuthenticationFile = "Tmdb/authentication.json";

    /// <summary><c>GET /3/configuration</c>: the image host and its sizes.</summary>
    public const string ConfigurationFile = "Tmdb/configuration.json";

    /// <summary><c>GET /3/movie/118340?append_to_response=credits,recommendations</c>: Guardians of the
    /// Galaxy (2014), in collection 284433, directed by James Gunn, with 20 recommendations.</summary>
    public const string MovieDetailsFile = "Tmdb/movie-details.json";

    /// <summary><c>GET /3/tv/73586?append_to_response=credits,recommendations</c>: Yellowstone (2018), five
    /// seasons, created by Taylor Sheridan and John Linson, with 20 recommendations.</summary>
    public const string TvDetailsFile = "Tmdb/tv-details.json";

    /// <summary><c>GET /3/collection/284433</c>: the three Guardians of the Galaxy films.</summary>
    public const string CollectionFile = "Tmdb/collection.json";

    /// <summary><c>GET /3/person/24/combined_credits</c>: Robert Zemeckis — 31 films directed, no show
    /// created, and his other jobs and his cast credits.</summary>
    public const string PersonCombinedCreditsFile = "Tmdb/person-combined-credits.json";

    /// <summary><c>GET /3/search/movie</c>: one result, "Landing Stripling" (1962), an animation.</summary>
    public const string SearchMovieFile = "Tmdb/search-movie.json";

    /// <summary><c>GET /3/search/tv</c>: one result, "Пінгвіни Мадагаскару" / "The Penguins of
    /// Madagascar" (2008).</summary>
    public const string SearchTvFile = "Tmdb/search-tv.json";

    /// <summary><c>GET /3/find/tt0071562?external_source=imdb_id</c>: The Godfather Part II, one film and
    /// no show.</summary>
    public const string FindImdbFile = "Tmdb/find-imdb.json";

    /// <summary><c>GET /3/discover/movie</c> of Documentary (99): the first page of 20 documentaries, the
    /// best-rated first, none with fewer than 300 votes.</summary>
    public const string DiscoverMovieDocumentaryFile = "Tmdb/discover-movie-documentary.json";

    /// <summary><c>GET /3/discover/tv</c> of Animation (16): the first page of 20 animated shows, the
    /// best-rated first, none with fewer than 300 votes.</summary>
    public const string DiscoverTvAnimationFile = "Tmdb/discover-tv-animation.json";

    /// <summary><c>GET /3/genre/movie/list</c>: TMDb's 19 movie genres, in Ukrainian.</summary>
    public const string GenresMovieFile = "Tmdb/genres-movie.json";

    /// <summary><c>GET /3/genre/tv/list</c>: TMDb's 16 TV genres, in Ukrainian.</summary>
    public const string GenresTvFile = "Tmdb/genres-tv.json";

    /// <summary>What TMDb answered a wrong token (recorded 2026-10-05): 401 with its JSON error,
    /// <c>status_code</c> 7.</summary>
    public const string WrongKeyResponseFile = "Tmdb/wrong-key-response.txt";

    /// <summary>MAM-34 — <c>GET /3/movie/223021?append_to_response=credits,recommendations,alternative_titles,external_ids</c>:
    /// "Landing Stripling" (1962), a Tom &amp; Jerry short — runtime 6, IMDb tt0056164 (also in <c>external_ids</c>), no
    /// collection, no alternative titles, Animation and Comedy.</summary>
    public const string MovieDetailsShortFile = "Tmdb/movie-details-short.json";

    /// <summary>MAM-34 — <c>GET /3/search/movie</c> "Landing Stripling" without a year: the one film, 223021.</summary>
    public const string SearchMovieWithoutYearFile = "Tmdb/search-movie-without-year.json";

    /// <summary>MAM-34 — <c>GET /3/search/tv</c> "Planet Earth": 20 of 41 shows, 1044 "Планета Земля" (2006) first,
    /// then II and III.</summary>
    public const string SearchTvPlanetEarthFile = "Tmdb/search-tv-planet-earth.json";

    /// <summary>MAM-34 — <c>GET /3/tv/1044?append_to_response=credits,recommendations,alternative_titles,external_ids</c>:
    /// Planet Earth (2006) — <c>episode_run_time</c> empty, <c>last_episode_to_air.runtime</c> 53, IMDb tt0795176 in
    /// <c>external_ids</c>, 16 alternative titles ("Planet Earth" three times), Documentary.</summary>
    public const string TvDetailsPlanetEarthFile = "Tmdb/tv-details-planet-earth.json";

    /// <summary>
    /// MAM-34 — <c>GET /3/tv/91724/season/1/episode/1?append_to_response=external_ids&amp;language=uk-UA</c> (recorded
    /// live on 2026-10-06): the first episode of "Greatest Events of World War II in
    /// Colour" — "Серія 1", aired 2019-07-12, 50 minutes —, its own IMDb id tt9103946 in <c>external_ids</c>.
    /// </summary>
    public const string TvEpisodeWwiiFile = "Tmdb/tv-episode-wwii-s01e01.json";

    /// <summary>MAM-34 — <c>GET /3/tv/1044/season/1/episode/8</c>, the same way: Planet Earth's "Джунглі", aired
    /// 2006-11-19, 51 minutes, IMDb id tt0905877.</summary>
    public const string TvEpisodePlanetEarthFile = "Tmdb/tv-episode-planet-earth-s01e08.json";

    /// <summary>MAM-34 — what TMDb answered an episode it doesn't have (<c>tv/91724/season/9/episode/99</c>): 404 with its
    /// JSON error, <c>status_code</c> 34.</summary>
    public const string TvEpisodeNotFoundResponseFile = "Tmdb/tv-episode-not-found-response.txt";

    /// <summary>The show of <see cref="TvEpisodeWwiiFile"/>.</summary>
    public const string TvEpisodeWwiiShowId = "91724";

    /// <summary>The episode's own IMDb id, from <c>external_ids</c> — the id Vasyl set by hand in Jellyfin on 2026-10-06.</summary>
    public const string TvEpisodeWwiiImdbId = "tt9103946";

    /// <summary>The episode of <see cref="TvEpisodePlanetEarthFile"/>'s own IMDb id.</summary>
    public const string TvEpisodePlanetEarthImdbId = "tt0905877";

    /// <summary>The film of <see cref="MovieDetailsShortFile"/>.</summary>
    public const string MovieDetailsShortId = "223021";

    /// <summary>Its IMDb id.</summary>
    public const string MovieDetailsShortImdbId = "tt0056164";

    /// <summary>The show of <see cref="TvDetailsPlanetEarthFile"/>.</summary>
    public const string TvDetailsPlanetEarthId = "1044";

    /// <summary>Its IMDb id, from <c>external_ids</c>.</summary>
    public const string TvDetailsPlanetEarthImdbId = "tt0795176";

    /// <summary>The film of <see cref="MovieDetailsFile"/>.</summary>
    public const string MovieDetailsId = "118340";

    /// <summary>Its IMDb id.</summary>
    public const string MovieDetailsImdbId = "tt2015381";

    /// <summary>Its collection, the one of <see cref="CollectionFile"/>.</summary>
    public const string CollectionId = "284433";

    /// <summary>Its director, James Gunn.</summary>
    public const string DirectorId = "15218";

    /// <summary>The show of <see cref="TvDetailsFile"/>.</summary>
    public const string TvDetailsId = "73586";

    /// <summary>The person of <see cref="PersonCombinedCreditsFile"/>.</summary>
    public const string PersonId = "24";

    /// <summary>The IMDb id <see cref="FindImdbFile"/> was asked with.</summary>
    public const string FindImdbId = "tt0071562";

    /// <summary>The film <see cref="FindImdbFile"/> finds.</summary>
    public const string FindMovieId = "240";

    /// <summary>The genres of <see cref="GenresMovieFile"/>.</summary>
    public const int MovieGenreCount = 19;

    /// <summary>The genres of <see cref="GenresTvFile"/>.</summary>
    public const int TvGenreCount = 16;

    private static readonly Lazy<string> MovieDetailsText = new(() => Read(MovieDetailsFile));
    private static readonly Lazy<string> TvDetailsText = new(() => Read(TvDetailsFile));
    private static readonly Lazy<string> CollectionText = new(() => Read(CollectionFile));
    private static readonly Lazy<string> PersonCombinedCreditsText = new(() => Read(PersonCombinedCreditsFile));
    private static readonly Lazy<string> SearchMovieText = new(() => Read(SearchMovieFile));
    private static readonly Lazy<string> SearchTvText = new(() => Read(SearchTvFile));
    private static readonly Lazy<string> FindImdbText = new(() => Read(FindImdbFile));
    private static readonly Lazy<string> DiscoverMovieDocumentaryText = new(() => Read(DiscoverMovieDocumentaryFile));
    private static readonly Lazy<string> DiscoverTvAnimationText = new(() => Read(DiscoverTvAnimationFile));
    private static readonly Lazy<string> GenresMovieText = new(() => Read(GenresMovieFile));
    private static readonly Lazy<string> GenresTvText = new(() => Read(GenresTvFile));
    private static readonly Lazy<string> MovieDetailsShortText = new(() => Read(MovieDetailsShortFile));
    private static readonly Lazy<string> SearchMovieWithoutYearText = new(() => Read(SearchMovieWithoutYearFile));
    private static readonly Lazy<string> SearchTvPlanetEarthText = new(() => Read(SearchTvPlanetEarthFile));
    private static readonly Lazy<string> TvDetailsPlanetEarthText = new(() => Read(TvDetailsPlanetEarthFile));
    private static readonly Lazy<string> TvEpisodeWwiiText = new(() => Read(TvEpisodeWwiiFile));

    /// <summary>The <see cref="MovieDetailsFile"/> sample, as TMDb sent it.</summary>
    public static string MovieDetails() => MovieDetailsText.Value;

    /// <summary>The <see cref="TvDetailsFile"/> sample, as TMDb sent it.</summary>
    public static string TvDetails() => TvDetailsText.Value;

    /// <summary>The <see cref="CollectionFile"/> sample, as TMDb sent it.</summary>
    public static string Collection() => CollectionText.Value;

    /// <summary>The <see cref="PersonCombinedCreditsFile"/> sample, as TMDb sent it.</summary>
    public static string PersonCombinedCredits() => PersonCombinedCreditsText.Value;

    /// <summary>The <see cref="SearchMovieFile"/> sample, as TMDb sent it.</summary>
    public static string SearchMovie() => SearchMovieText.Value;

    /// <summary>The <see cref="SearchTvFile"/> sample, as TMDb sent it.</summary>
    public static string SearchTv() => SearchTvText.Value;

    /// <summary>The <see cref="FindImdbFile"/> sample, as TMDb sent it.</summary>
    public static string FindImdb() => FindImdbText.Value;

    /// <summary>The <see cref="DiscoverMovieDocumentaryFile"/> sample, as TMDb sent it.</summary>
    public static string DiscoverMovieDocumentary() => DiscoverMovieDocumentaryText.Value;

    /// <summary>The <see cref="DiscoverTvAnimationFile"/> sample, as TMDb sent it.</summary>
    public static string DiscoverTvAnimation() => DiscoverTvAnimationText.Value;

    /// <summary>The <see cref="GenresMovieFile"/> sample, as TMDb sent it.</summary>
    public static string GenresMovie() => GenresMovieText.Value;

    /// <summary>The <see cref="GenresTvFile"/> sample, as TMDb sent it.</summary>
    public static string GenresTv() => GenresTvText.Value;

    /// <summary>The <see cref="MovieDetailsShortFile"/> sample, as TMDb sent it.</summary>
    public static string MovieDetailsShort() => MovieDetailsShortText.Value;

    /// <summary>The <see cref="SearchMovieWithoutYearFile"/> sample, as TMDb sent it.</summary>
    public static string SearchMovieWithoutYear() => SearchMovieWithoutYearText.Value;

    /// <summary>The <see cref="SearchTvPlanetEarthFile"/> sample, as TMDb sent it.</summary>
    public static string SearchTvPlanetEarth() => SearchTvPlanetEarthText.Value;

    /// <summary>The <see cref="TvDetailsPlanetEarthFile"/> sample, as TMDb sent it.</summary>
    public static string TvDetailsPlanetEarth() => TvDetailsPlanetEarthText.Value;

    /// <summary>The <see cref="TvEpisodeWwiiFile"/> sample, as TMDb sent it.</summary>
    public static string TvEpisodeWwii() => TvEpisodeWwiiText.Value;

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, relativePath));
}
