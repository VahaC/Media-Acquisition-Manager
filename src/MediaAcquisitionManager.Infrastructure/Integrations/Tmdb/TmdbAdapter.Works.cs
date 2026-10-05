using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

// MAM-33 — the catalog's single resources: a work's details with its credits and recommendations in one
// call, a collection with its parts, a person with the works they directed or created.
internal sealed partial class TmdbAdapter
{
    /// <summary>
    /// <c>GET /3/movie/{id}</c> or <c>GET /3/tv/{id}</c> with <c>append_to_response=credits,recommendations</c>:
    /// the details, the crew and TMDb's first page of recommendations in one request instead of three.
    /// </summary>
    public Task<IntegrationCallResult<CatalogWork>> GetWorkAsync(
        IntegrationConnectionSettings settings, MediaWorkKind kind, string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // A caller's mistake, not TMDb's answer: nothing is sent.
        var segment = KindSegment(kind);
        RequireTmdbId(id, nameof(id));

        return CallAsync<CatalogWork>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync(
                client,
                settings,
                sessionSecrets,
                $"{segment}/{id}?append_to_response=credits,recommendations&language={CatalogLanguage}",
                WorkNotFoundError(kind, id),
                (root, secrets) => ReadWork(root, kind, secrets),
                token),
            cancellationToken);
    }

    /// <summary><c>GET /3/collection/{id}</c>: its name and every part, those still to come included.</summary>
    public Task<IntegrationCallResult<CatalogCollection>> GetCollectionAsync(
        IntegrationConnectionSettings settings, string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RequireTmdbId(id, nameof(id));

        return CallAsync<CatalogCollection>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync(
                client,
                settings,
                sessionSecrets,
                $"collection/{id}?language={CatalogLanguage}",
                $"TMDb has no collection with the id {id}.",
                ReadCollection,
                token),
            cancellationToken);
    }

    /// <summary>
    /// <c>GET /3/person/{id}/combined_credits</c>: the films they directed and the shows they created, in
    /// one request for both kinds.
    /// </summary>
    /// <remarks>
    /// The answer carries no name, and the person's own <c>GET /3/person/{id}</c> would be a second
    /// request for it: <see cref="CatalogPerson.Name"/> is left empty. Whoever asks for a person found them
    /// in a work's credits (<see cref="CatalogWork.Directors"/>), which name them.
    /// </remarks>
    public Task<IntegrationCallResult<CatalogPerson>> GetPersonAsync(
        IntegrationConnectionSettings settings, string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        RequireTmdbId(id, nameof(id));

        return CallAsync<CatalogPerson>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync(
                client,
                settings,
                sessionSecrets,
                $"person/{id}/combined_credits?language={CatalogLanguage}",
                $"TMDb has no person with the id {id}.",
                (root, secrets) => ReadPerson(root, id, secrets),
                token),
            cancellationToken);
    }

    private static string WorkNotFoundError(MediaWorkKind kind, string id) =>
        kind == MediaWorkKind.Series ? $"TMDb has no show with the id {id}." : $"TMDb has no movie with the id {id}.";

    /// <summary>
    /// A film's or a show's details. A film: its IMDb id, its collection and its directors (the crew's
    /// <c>Director</c> jobs). A show: its creators (<c>created_by</c>) and its seasons; TMDb's show details
    /// carry no IMDb id without <c>external_ids</c>, which nothing here needs, so it stays <c>null</c>.
    /// Either: TMDb's recommendations of the same kind.
    /// </summary>
    private static CatalogWork? ReadWork(JsonElement root, MediaWorkKind kind, string?[] secrets)
    {
        if (ReadSummary(root, kind, secrets) is not { } summary)
            return null;

        var isShow = kind == MediaWorkKind.Series;
        return new CatalogWork(
            summary,
            isShow ? null : ImdbIdOf(root),
            isShow || !root.TryGetProperty("belongs_to_collection", out var collection) ? null : ReferenceOf(collection, secrets),
            isShow ? Creators(root, secrets) : Directors(root, secrets),
            isShow ? Seasons(root) : [],
            root.TryGetProperty("recommendations", out var recommendations) && recommendations.ValueKind == JsonValueKind.Object
                ? ReadSummaries(recommendations, "results", kind, secrets) ?? []
                : []);
    }

    /// <summary>A film's <c>imdb_id</c> when it is one (<see cref="MediaIdentifierRules.IsImdbId"/>);
    /// TMDb sends <c>null</c> or <c>""</c> for a film without.</summary>
    private static string? ImdbIdOf(JsonElement root) =>
        root.TryGetProperty("imdb_id", out var imdb)
        && imdb.ValueKind == JsonValueKind.String
        && imdb.GetString() is { } value
        && MediaIdentifierRules.IsImdbId(value)
            ? value
            : null;

    /// <summary>The crew's directors, each once and in the crew's order: a co-director credited twice, in
    /// two departments, is one director.</summary>
    private static List<CatalogReference> Directors(JsonElement root, string?[] secrets)
    {
        var directors = new List<CatalogReference>();
        if (!root.TryGetProperty("credits", out var credits)
            || credits.ValueKind != JsonValueKind.Object
            || !credits.TryGetProperty("crew", out var crew)
            || crew.ValueKind != JsonValueKind.Array)
            return directors;

        foreach (var member in crew.EnumerateArray())
        {
            if (member.ValueKind == JsonValueKind.Object
                && IsJob(member, "Director")
                && ReferenceOf(member, secrets) is { } director
                && !directors.Exists(known => string.Equals(known.Id, director.Id, StringComparison.Ordinal)))
                directors.Add(director);
        }

        return directors;
    }

    /// <summary>A show's creators (<c>created_by</c>), each once and in TMDb's order.</summary>
    private static List<CatalogReference> Creators(JsonElement root, string?[] secrets)
    {
        var creators = new List<CatalogReference>();
        if (!root.TryGetProperty("created_by", out var createdBy) || createdBy.ValueKind != JsonValueKind.Array)
            return creators;

        foreach (var entry in createdBy.EnumerateArray())
        {
            if (ReferenceOf(entry, secrets) is { } creator
                && !creators.Exists(known => string.Equals(known.Id, creator.Id, StringComparison.Ordinal)))
                creators.Add(creator);
        }

        return creators;
    }

    /// <summary>
    /// A show's seasons as TMDb lists them, the specials (season 0) included: each once by its number, and
    /// only one with a number. A season TMDb has no air date for yet has none; an episode count it doesn't
    /// give is 0.
    /// </summary>
    private static List<CatalogSeason> Seasons(JsonElement root)
    {
        var seasons = new List<CatalogSeason>();
        if (!root.TryGetProperty("seasons", out var seasonsElement) || seasonsElement.ValueKind != JsonValueKind.Array)
            return seasons;

        foreach (var season in seasonsElement.EnumerateArray())
        {
            if (season.ValueKind == JsonValueKind.Object
                && season.TryGetProperty("season_number", out var numberElement)
                && numberElement.ValueKind == JsonValueKind.Number
                && numberElement.TryGetInt32(out var number)
                && number >= 0
                && !seasons.Exists(known => known.Number == number))
                seasons.Add(new CatalogSeason(number, DateProperty(season, "air_date"), CountProperty(season, "episode_count")));
        }

        return seasons;
    }

    /// <summary>A collection: its id and name, and its parts as films. One without an id or a parts list
    /// is not TMDb's answer.</summary>
    private static CatalogCollection? ReadCollection(JsonElement root, string?[] secrets)
    {
        if (IdOf(root) is not { } id || ReadSummaries(root, "parts", MediaWorkKind.Movie, secrets) is not { } parts)
            return null;
        return new CatalogCollection(id, TextProperty(root, "name", NameMaximumLength, secrets), parts);
    }

    /// <summary>
    /// A person's works from their crew credits: each film they are credited as <c>Director</c> of, and
    /// each show as <c>Creator</c> of, once — a crew lists one entry per job, and a film both directed and
    /// written by them is still one work. Their cast credits and every other job are not read.
    /// </summary>
    private static CatalogPerson? ReadPerson(JsonElement root, string id, string?[] secrets)
    {
        if (!root.TryGetProperty("crew", out var crew) || crew.ValueKind != JsonValueKind.Array)
            return null;

        var works = new List<CatalogWorkSummary>();
        foreach (var entry in crew.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("media_type", out var type) || type.ValueKind != JsonValueKind.String)
                continue;

            MediaWorkKind? kind = type.GetString() switch
            {
                "movie" when IsJob(entry, "Director") => MediaWorkKind.Movie,
                "tv" when IsJob(entry, "Creator") => MediaWorkKind.Series,
                _ => null,
            };
            if (kind is { } workKind
                && ReadSummary(entry, workKind, secrets) is { } work
                && !works.Exists(known => known.Kind == work.Kind && string.Equals(known.Id, work.Id, StringComparison.Ordinal)))
                works.Add(work);
        }

        // The id asked for: the answer's own id is the same person, and an answer without one is still theirs.
        return new CatalogPerson(id, "", works);
    }

    /// <summary>Whether a crew entry's <c>job</c> is <paramref name="job"/>, as TMDb spells it.</summary>
    private static bool IsJob(JsonElement entry, string job) =>
        entry.TryGetProperty("job", out var jobElement)
        && jobElement.ValueKind == JsonValueKind.String
        && string.Equals(jobElement.GetString(), job, StringComparison.Ordinal);
}
