using System.Globalization;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

// MAM-33 — the catalog's single resources: a work's details with its credits and recommendations in one
// call, a collection with its parts, a person with the works they directed or created. MAM-34 — the same call
// also brings a work's other titles and its external ids, for the owner linking a library work to it; and one
// episode of a show with its own IMDb id, for the owner linking a film item to that episode. And a work's overview in
// the language of whoever opens its description on the Recommendations page.
internal sealed partial class TmdbAdapter
{
    /// <summary>
    /// MAM-34 — the most alternative titles a work's details keep: a library work's titles are compared with every
    /// one of them when its suggestions are ranked, and the tail of a long list is other countries' spellings of the
    /// titles already kept. The live check found 16 for Planet Earth (VendorSamples); 50 leaves room for a famous
    /// film's longer list. A constant, not a setting (the house rule for a tuned number).
    /// </summary>
    public const int AlternativeTitleMaximumCount = 50;

    /// <summary>
    /// <c>GET /3/movie/{id}</c> or <c>GET /3/tv/{id}</c> with
    /// <c>append_to_response=credits,recommendations,alternative_titles,external_ids</c>: the details, the crew,
    /// TMDb's first page of recommendations and — MAM-34 — the work's other titles and a show's IMDb id, in one
    /// request instead of five.
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
                $"{segment}/{id}?append_to_response=credits,recommendations,alternative_titles,external_ids&language={CatalogLanguage}",
                WorkNotFoundError(kind, id),
                (root, secrets) => ReadWork(root, kind, secrets),
                token),
            cancellationToken);
    }

    /// <summary>
    /// <c>GET /3/{movie|tv}/{id}?language={language}</c> with nothing appended: the details in that language, of which
    /// only the <c>overview</c> is read. TMDb answers <c>""</c> for a work it has no overview of in the language asked
    /// (movie-details-short.json) — it never falls back to another language there — which is no overview.
    /// </summary>
    public Task<IntegrationCallResult<CatalogOverview>> GetOverviewAsync(
        IntegrationConnectionSettings settings, MediaWorkKind kind, string id, string language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(language);
        // A caller's mistake, not TMDb's answer: nothing is sent, and nothing but a language code reaches the query.
        var segment = KindSegment(kind);
        RequireTmdbId(id, nameof(id));
        if (!RecommendationOverviewRules.IsLanguage(language))
            throw new ArgumentException($"“{language}” is not a language code such as en or uk-UA.", nameof(language));

        return CallAsync<CatalogOverview>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync(
                client,
                settings,
                sessionSecrets,
                $"{segment}/{id}?language={language}",
                WorkNotFoundError(kind, id),
                (root, secrets) =>
                {
                    var overview = TextProperty(root, "overview", CatalogWorkEntity.OverviewMaximumLength, secrets);
                    return new CatalogOverview(overview.Length > 0 ? overview : null);
                },
                token),
            cancellationToken);
    }

    /// <summary>
    /// MAM-34 — <c>GET /3/tv/{id}/season/{season}/episode/{episode}</c> with <c>append_to_response=external_ids</c>: one
    /// episode of a show and its own IMDb id (<c>external_ids.imdb_id</c>), the one id of the episode a film item can
    /// be given — Jellyfin lists a documentary's episodes as movies, whose <c>Tmdb</c> key names a film. Its title,
    /// air date and runtime come along; its crew, guests and stills are not read. TMDb's own 404 is a show, a season or
    /// an episode it doesn't know.
    /// </summary>
    public Task<IntegrationCallResult<CatalogEpisode>> GetEpisodeAsync(
        IntegrationConnectionSettings settings, string showId, int seasonNumber, int episodeNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Caller's mistakes, not TMDb's answer: nothing is sent.
        RequireTmdbId(showId, nameof(showId));
        ArgumentOutOfRangeException.ThrowIfNegative(seasonNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(episodeNumber);

        return CallAsync<CatalogEpisode>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync(
                client,
                settings,
                sessionSecrets,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"tv/{showId}/season/{seasonNumber}/episode/{episodeNumber}?append_to_response=external_ids&language={CatalogLanguage}"),
                string.Create(CultureInfo.InvariantCulture, $"TMDb has no episode {episodeNumber} of season {seasonNumber} of the show {showId}."),
                ReadEpisode,
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
    /// <c>Director</c> jobs). A show: its creators (<c>created_by</c>) and its seasons, and — MAM-34 — its IMDb id
    /// from <c>external_ids</c>, which TMDb's show details carry only when asked to append them (the lookup's
    /// details don't ask: its show has none). Either: TMDb's recommendations of the same kind, and — MAM-34 — its
    /// runtime, its overview and its other titles.
    /// </summary>
    private static CatalogWork? ReadWork(JsonElement root, MediaWorkKind kind, string?[] secrets)
    {
        if (ReadSummary(root, kind, secrets) is not { } summary)
            return null;

        var isShow = kind == MediaWorkKind.Series;
        var overview = TextProperty(root, "overview", CatalogWorkEntity.OverviewMaximumLength, secrets);
        return new CatalogWork(
            summary,
            isShow ? ExternalImdbIdOf(root) : ImdbIdOf(root),
            isShow || !root.TryGetProperty("belongs_to_collection", out var collection) ? null : ReferenceOf(collection, secrets),
            isShow ? Creators(root, secrets) : Directors(root, secrets),
            isShow ? Seasons(root) : [],
            root.TryGetProperty("recommendations", out var recommendations) && recommendations.ValueKind == JsonValueKind.Object
                ? ReadSummaries(recommendations, "results", kind, secrets) ?? []
                : [],
            isShow ? ShowRuntime(root) : MinutesProperty(root, "runtime"),
            // TMDb's "" for a work it has no overview of in the language asked (movie-details-short.json).
            overview.Length > 0 ? overview : null,
            AlternativeTitles(root, isShow, secrets));
    }

    /// <summary>
    /// MAM-34 — an episode as TMDb answers it: its numbers — an answer without them isn't TMDb's —, its title (TMDb's
    /// own fallback when it has none in Ukrainian), air date and runtime, and its IMDb id from <c>external_ids</c>, as a
    /// show's is read (an episode's details have no <c>imdb_id</c> of their own).
    /// </summary>
    private static CatalogEpisode? ReadEpisode(JsonElement root, string?[] secrets)
    {
        if (WholeNumber(root, "season_number") is not { } season || WholeNumber(root, "episode_number") is not { } episode)
            return null;

        var title = TextProperty(root, "name", TitleMaximumLength, secrets);
        return new CatalogEpisode(
            season,
            episode,
            title.Length > 0 ? title : null,
            DateProperty(root, "air_date"),
            MinutesProperty(root, "runtime"),
            ExternalImdbIdOf(root));
    }

    /// <summary>MAM-34 — a whole number of zero or more; <c>null</c> when absent or anything else.</summary>
    private static int? WholeNumber(JsonElement element, string name) =>
        element.TryGetProperty(name, out var numberElement)
        && numberElement.ValueKind == JsonValueKind.Number
        && numberElement.TryGetInt32(out var number)
        && number >= 0
            ? number
            : null;

    /// <summary>A film's <c>imdb_id</c> when it is one (<see cref="MediaIdentifierRules.IsImdbId"/>);
    /// TMDb sends <c>null</c> or <c>""</c> for a film without.</summary>
    private static string? ImdbIdOf(JsonElement root) =>
        root.TryGetProperty("imdb_id", out var imdb)
        && imdb.ValueKind == JsonValueKind.String
        && imdb.GetString() is { } value
        && MediaIdentifierRules.IsImdbId(value)
            ? value
            : null;

    /// <summary>
    /// MAM-34 — a show's IMDb id, or an episode's: <c>external_ids.imdb_id</c> when it is one
    /// (tv-details-planet-earth.json's tt0795176); neither's details have an <c>imdb_id</c> of their own. The other
    /// registries' ids beside it (TheTVDB's, Wikidata's …) are not read.
    /// </summary>
    private static string? ExternalImdbIdOf(JsonElement root) =>
        root.TryGetProperty("external_ids", out var externalIds) && externalIds.ValueKind == JsonValueKind.Object
            ? ImdbIdOf(externalIds)
            : null;

    /// <summary>
    /// MAM-34 — a show's runtime: the first of <c>episode_run_time</c>, else its latest episode's
    /// (<c>last_episode_to_air.runtime</c>). TMDb has left that list empty for shows (tv-details.json and
    /// tv-details-planet-earth.json both send <c>[]</c>), so the latest episode is what it says today: 88 minutes
    /// for Yellowstone, 53 for Planet Earth.
    /// </summary>
    private static int? ShowRuntime(JsonElement root)
    {
        if (root.TryGetProperty("episode_run_time", out var runTimes) && runTimes.ValueKind == JsonValueKind.Array)
        {
            foreach (var runTime in runTimes.EnumerateArray())
            {
                if (runTime.ValueKind == JsonValueKind.Number && runTime.TryGetInt32(out var minutes) && minutes > 0)
                    return minutes;
            }
        }

        return root.TryGetProperty("last_episode_to_air", out var lastEpisode) && lastEpisode.ValueKind == JsonValueKind.Object
            ? MinutesProperty(lastEpisode, "runtime")
            : null;
    }

    /// <summary>MAM-34 — a runtime in whole minutes; <c>null</c> when absent, TMDb's <c>0</c> for one it doesn't
    /// know, or anything else.</summary>
    private static int? MinutesProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var minutesElement)
        && minutesElement.ValueKind == JsonValueKind.Number
        && minutesElement.TryGetInt32(out var minutes)
        && minutes > 0
            ? minutes
            : null;

    /// <summary>
    /// MAM-34 — the other titles TMDb knows a work by: a film's <c>alternative_titles.titles</c>, a show's
    /// <c>alternative_titles.results</c> (the two shapes differ: movie-details-short.json, tv-details-planet-earth.json).
    /// Each cleaned (<see cref="RemoteText.Clean"/>), in TMDb's order, each once whatever its letter case — Planet
    /// Earth's list names "Planet Earth" for three countries — and at most <see cref="AlternativeTitleMaximumCount"/>.
    /// Their countries and types are not read.
    /// </summary>
    private static List<string> AlternativeTitles(JsonElement root, bool isShow, string?[] secrets)
    {
        var titles = new List<string>();
        if (!root.TryGetProperty("alternative_titles", out var alternativeTitles)
            || alternativeTitles.ValueKind != JsonValueKind.Object
            || !alternativeTitles.TryGetProperty(isShow ? "results" : "titles", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            return titles;

        foreach (var entry in entries.EnumerateArray())
        {
            if (titles.Count == AlternativeTitleMaximumCount)
                break;
            if (entry.ValueKind != JsonValueKind.Object)
                continue;
            var title = TextProperty(entry, "title", TitleMaximumLength, secrets);
            if (title.Length > 0 && !titles.Exists(known => string.Equals(known, title, StringComparison.OrdinalIgnoreCase)))
                titles.Add(title);
        }

        return titles;
    }

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
