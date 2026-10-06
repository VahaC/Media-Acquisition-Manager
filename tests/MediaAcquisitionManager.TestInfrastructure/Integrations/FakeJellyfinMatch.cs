using System.Text.Json.Nodes;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-34 — one result a <see cref="FakeJellyfinServer"/>'s metadata provider has for an id: what Jellyfin's
/// <c>POST /Items/RemoteSearch/{Movie|Series}</c> answers when asked for it, and what Apply gives the item it is applied to.
/// </summary>
/// <param name="Endpoint">The search it answers: <c>Movie</c> or <c>Series</c>. An id is found only through the endpoint of
/// its own kind — and, as recorded live, a TV id searched as a film answers whatever film has that number.</param>
/// <param name="Provider">The provider's name as the search names it: <c>TheMovieDb</c> or <c>The Open Movie
/// Database</c>.</param>
/// <param name="Key">The key of the id the search is by, in Jellyfin's spelling (<c>Tmdb</c>, <c>Imdb</c>): matched exactly,
/// so the lower-case <c>tmdb</c> the live check sent answers no rows.</param>
/// <param name="Value">The id the search is by.</param>
/// <param name="Row">The result as the live check recorded one: <c>Name</c>, <c>ProviderIds</c>, <c>ProductionYear</c>,
/// <c>PremiereDate</c>, <c>ImageUrl</c>, <c>SearchProviderName</c>, <c>Overview</c> (not an Open Movie Database row's) and
/// <c>Artists</c>.</param>
/// <param name="NamesByLanguage">The name in a language the search body asks for (<c>SearchInfo.MetadataLanguage</c>) and an
/// item whose library has one is given by Apply; <c>Row</c>'s <c>Name</c> is the server's default, English.</param>
/// <param name="OverviewsByLanguage">The same for the overview.</param>
public sealed record FakeJellyfinMatch(
    string Endpoint,
    string Provider,
    string Key,
    string Value,
    JsonObject Row,
    IReadOnlyDictionary<string, string>? NamesByLanguage = null,
    IReadOnlyDictionary<string, string>? OverviewsByLanguage = null);
