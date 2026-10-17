using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

// MAM-33 — the lookup a match runs (IMetadataResolver): which TMDb works a title or a registry id may
// name, each with the evidence it matched by. It never picks one; the caller does.
internal sealed partial class TmdbAdapter
{
    /// <inheritdoc/>
    /// <remarks>A film's TMDb id, a show's TMDb TV id, an IMDb id of either, and a title.</remarks>
    public MetadataResolverCapabilities Capabilities { get; } = new(
        [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb, MediaIdentifierScheme.TmdbTv],
        SearchesByTitle: true);

    /// <summary>
    /// By the work's own TMDb id when the query has one of its kind (its details), else by its IMDb id
    /// (a find), else by its title and year (a search): the strongest evidence the query offers, one
    /// request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TMDb's own id first: it names the record itself, where an IMDb id is filed under it by TMDb and
    /// may be missing or shared. A TMDb id of the other kind is not the work's — TMDb numbers films and
    /// shows apart (ADR 0017) — and is not used. A malformed id of a scheme used is the caller's mistake
    /// and throws, as an episode does; nothing is sent.
    /// </para>
    /// <para>
    /// A work the id names that TMDb doesn't have is no candidate, not a failure: the lookup answers
    /// what it found, which is nothing, as a search with no results does. A search keeps only the results
    /// whose title or original title has the query's key (<see cref="TitleMatchKey"/>): with the query's
    /// year as their release year they matched by title and year, otherwise by title only (specification
    /// §3.1).
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<MetadataCandidate>>> LookupAsync(
        IntegrationConnectionSettings settings, MetadataQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Identifiers, nameof(query));
        var segment = KindSegment(query.Kind);
        var tmdbId = IdentifierOf(query.Identifiers, query.Kind == MediaWorkKind.Series ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb);
        var imdbId = IdentifierOf(query.Identifiers, MediaIdentifierScheme.Imdb);

        if (tmdbId is not null)
            return LookupByTmdbIdAsync(settings, query.Kind, segment, tmdbId, cancellationToken);
        if (imdbId is not null)
            return LookupByImdbIdAsync(settings, query.Kind, imdbId, cancellationToken);

        if (query.Title is null || TitleMatchKey.For(query.Title).Length == 0)
            throw new ArgumentException("A lookup needs a TMDb or IMDb id, or a title with a letter or a digit.", nameof(query));
        var pathAndQuery = SearchPathAndQuery(query.Kind, query.Title, query.Year, LanguageAskedBy(settings));
        return LookupByTitleAsync(settings, query, pathAndQuery, cancellationToken);
    }

    /// <summary>The work's details without the credits and recommendations a lookup doesn't need: one
    /// candidate, or none when TMDb has no such work.</summary>
    private async Task<IntegrationCallResult<IReadOnlyList<MetadataCandidate>>> LookupByTmdbIdAsync(
        IntegrationConnectionSettings settings, MediaWorkKind kind, string segment, string id, CancellationToken cancellationToken)
    {
        var details = await CallAsync<CatalogWork>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync(
                client,
                settings,
                sessionSecrets,
                $"{segment}/{id}?language={LanguageAskedBy(settings)}",
                WorkNotFoundError(kind, id),
                (root, secrets) => ReadWork(root, kind, secrets),
                token),
            cancellationToken);

        return details switch
        {
            { IsSuccess: true, Value: { } work } => Candidates([Candidate(work.Summary, work.ImdbId, MatchEvidence.Identifier)]),
            { Status: IntegrationCallStatus.NotFound } => Candidates([]),
            _ => IntegrationCallResult<IReadOnlyList<MetadataCandidate>>.Failed(details.Status, details.Error!),
        };
    }

    /// <summary>The works of the query's kind TMDb files under the IMDb id, each carrying that id.</summary>
    private async Task<IntegrationCallResult<IReadOnlyList<MetadataCandidate>>> LookupByImdbIdAsync(
        IntegrationConnectionSettings settings, MediaWorkKind kind, string imdbId, CancellationToken cancellationToken)
    {
        var found = await CallAsync<IReadOnlyList<CatalogWorkSummary>>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync<IReadOnlyList<CatalogWorkSummary>>(
                client, settings, sessionSecrets, FindPathAndQuery(imdbId, LanguageAskedBy(settings)), notFoundError: null, ReadFound, token),
            cancellationToken);

        return found.IsSuccess
            ? Candidates([.. found.Value!.Where(work => work.Kind == kind).Select(work => Candidate(work, imdbId, MatchEvidence.Identifier))])
            : IntegrationCallResult<IReadOnlyList<MetadataCandidate>>.Failed(found.Status, found.Error!);
    }

    /// <summary>The search's first page, kept to the results whose title key matches the query's.</summary>
    private async Task<IntegrationCallResult<IReadOnlyList<MetadataCandidate>>> LookupByTitleAsync(
        IntegrationConnectionSettings settings, MetadataQuery query, string pathAndQuery, CancellationToken cancellationToken)
    {
        var results = await CallAsync<IReadOnlyList<CatalogWorkSummary>>(
            settings,
            (client, sessionSecrets, token) => GetJsonAsync<IReadOnlyList<CatalogWorkSummary>>(
                client,
                settings,
                sessionSecrets,
                pathAndQuery,
                notFoundError: null,
                (root, secrets) => ReadSummaries(root, "results", query.Kind, secrets),
                token),
            cancellationToken);
        if (!results.IsSuccess)
            return IntegrationCallResult<IReadOnlyList<MetadataCandidate>>.Failed(results.Status, results.Error!);

        var key = TitleMatchKey.For(query.Title!);
        var candidates = new List<MetadataCandidate>();
        foreach (var work in results.Value!)
        {
            var titleMatches = string.Equals(TitleMatchKey.For(work.Title), key, StringComparison.Ordinal)
                || (work.OriginalTitle is { } original && string.Equals(TitleMatchKey.For(original), key, StringComparison.Ordinal));
            if (!titleMatches)
                continue;

            var evidence = query.Year is { } year && work.ReleaseDate?.Year == year ? MatchEvidence.TitleAndYear : MatchEvidence.TitleOnly;
            candidates.Add(Candidate(work, null, evidence));
        }

        return Candidates(candidates);
    }

    /// <summary>
    /// The query's first id of <paramref name="scheme"/>, or <c>null</c>. Throws for one that isn't an id
    /// of the scheme: it would go into the request's path. Ids of a scheme this lookup doesn't use (TVDB)
    /// are not looked at.
    /// </summary>
    private static string? IdentifierOf(IReadOnlyList<MediaIdentifier> identifiers, MediaIdentifierScheme scheme)
    {
        foreach (var identifier in identifiers)
        {
            ArgumentNullException.ThrowIfNull(identifier, nameof(identifiers));
            if (identifier.Scheme != scheme)
                continue;

            var valid = identifier.Value is not null
                && (scheme == MediaIdentifierScheme.Imdb ? MediaIdentifierRules.IsImdbId(identifier.Value) : MediaIdentifierRules.IsTmdbId(identifier.Value));
            if (!valid)
                throw new ArgumentException($"An identifier is not a well-formed {scheme} id.", nameof(identifiers));
            return identifier.Value;
        }

        return null;
    }

    /// <summary>
    /// A listed work as a candidate: its TMDb id as the record's id, its catalog title (and its original
    /// one when it differs) with its release year, and the registry ids known for it.
    /// </summary>
    private static MetadataCandidate Candidate(CatalogWorkSummary work, string? imdbId, MatchEvidence evidence)
    {
        var year = work.ReleaseDate?.Year;
        List<ReportedTitle> titles = [new ReportedTitle(work.Title, MediaTitleKind.Display, year)];
        if (work.OriginalTitle is { } original && !string.Equals(original, work.Title, StringComparison.Ordinal))
            titles.Add(new ReportedTitle(original, MediaTitleKind.Original, year));

        List<MediaIdentifier> identifiers =
            [new MediaIdentifier(work.Kind == MediaWorkKind.Series ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb, work.Id)];
        if (imdbId is not null)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId));

        return new MetadataCandidate(work.Id, titles, identifiers, evidence);
    }

    private static IntegrationCallResult<IReadOnlyList<MetadataCandidate>> Candidates(List<MetadataCandidate> candidates) =>
        IntegrationCallResult<IReadOnlyList<MetadataCandidate>>.Succeeded(candidates);
}
