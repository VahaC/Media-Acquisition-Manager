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
/// <param name="ImdbId">A film's <c>imdb_id</c>; none when <c>null</c>. A show's details carry none.</param>
/// <param name="CollectionId">A film's collection (<c>belongs_to_collection</c>), named by
/// <see cref="FakeTmdbServer.AddCollection"/>.</param>
/// <param name="DirectorIds">A film's directors (its crew's <c>Director</c> jobs), or a show's creators
/// (<c>created_by</c>), named by <see cref="FakeTmdbServer.AddPerson"/>.</param>
/// <param name="Seasons">A show's seasons; none when <c>null</c>.</param>
/// <param name="RecommendationIds">The works of the same kind TMDb recommends beside it, best first; each
/// must be added too.</param>
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
    int[]? RecommendationIds = null);
