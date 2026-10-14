using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-33 — one film or show of a <see cref="FakeTmdbServer"/>'s catalog, in the terms TMDb lists it by.
/// </summary>
/// <param name="Kind">A film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>):
/// TMDb numbers the two apart, so one <paramref name="Id"/> may be both.</param>
/// <param name="Id">TMDb's id of it.</param>
/// <param name="Title">Its <c>title</c> (a show's <c>name</c>), in the catalog's language.</param>
/// <param name="OriginalTitle">Its <c>original_title</c> (<c>original_name</c>); <paramref name="Title"/>
/// when <c>null</c>, as TMDb gives one for every work.</param>
/// <param name="ReleaseDate">Its <c>release_date</c> (<c>first_air_date</c>) as TMDb writes it; <c>null</c>
/// is sent as TMDb's <c>""</c>, a date not known yet.</param>
/// <param name="GenreIds">TMDb's ids of its genres; none when <c>null</c>.</param>
/// <param name="Rating">Its <c>vote_average</c>.</param>
/// <param name="VoteCount">Its <c>vote_count</c>.</param>
/// <param name="PosterPath">Its <c>poster_path</c>, such as <c>/abc.jpg</c>; none when <c>null</c>.</param>
/// <param name="ImdbId">A film's <c>imdb_id</c>, and — MAM-34 — a film's or a show's <c>external_ids.imdb_id</c> when
/// the details are asked to append them (a show's details carry no <c>imdb_id</c> of their own); none when
/// <c>null</c>.</param>
/// <param name="CollectionId">A film's collection (<c>belongs_to_collection</c>), named by
/// <see cref="FakeTmdbServer.AddCollection"/>.</param>
/// <param name="DirectorIds">A film's directors (its crew's <c>Director</c> jobs), or a show's creators
/// (<c>created_by</c>), named by <see cref="FakeTmdbServer.AddPerson"/>.</param>
/// <param name="Seasons">A show's seasons; none when <c>null</c>.</param>
/// <param name="RecommendationIds">The works of the same kind TMDb recommends beside it, best first; each
/// must be added too.</param>
/// <param name="RuntimeMinutes">MAM-34 — a film's <c>runtime</c>, or a show's latest episode's
/// (<c>last_episode_to_air.runtime</c>, its <c>episode_run_time</c> left empty as TMDb sends it today); <c>null</c> is
/// sent as TMDb's <c>0</c> for a film and no latest episode for a show.</param>
/// <param name="Overview">MAM-34 — its <c>overview</c>, in its details and in lists; <c>null</c> is sent as TMDb's
/// <c>""</c>.</param>
/// <param name="AlternativeTitles">MAM-34 — the titles of its <c>alternative_titles</c> (a film's <c>titles</c>, a
/// show's <c>results</c>), when the details are asked to append them; none when <c>null</c>.</param>
/// <param name="OverviewsByLanguage">Its details' <c>overview</c> by the <c>language</c> they are asked in (<c>en</c>,
/// <c>uk-UA</c>); a language not in it answers TMDb's <c>""</c>, as TMDb does for one it has no overview in. When
/// <c>null</c>, <paramref name="Overview"/> answers whatever the language.</param>
public sealed record FakeTmdbWork(
    MediaWorkKind Kind,
    int Id,
    string Title,
    string? OriginalTitle = null,
    string? ReleaseDate = "2000-01-01",
    int[]? GenreIds = null,
    double Rating = 7.5,
    int VoteCount = 1000,
    string? PosterPath = null,
    string? ImdbId = null,
    int? CollectionId = null,
    int[]? DirectorIds = null,
    FakeTmdbSeason[]? Seasons = null,
    int[]? RecommendationIds = null,
    int? RuntimeMinutes = null,
    string? Overview = null,
    string[]? AlternativeTitles = null,
    IReadOnlyDictionary<string, string>? OverviewsByLanguage = null);
