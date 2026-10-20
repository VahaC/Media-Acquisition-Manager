using System.Globalization;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — the dialog's two ways past the prepared suggestions: the owner's own search, and an id the owner pasted. Both
// ask the catalog now — the owner is looking — and both answer candidates ranked the way the suggestions are.
public sealed partial class LibraryLinkSuggestionService
{
    private static readonly Message TitleMissingError = Message.Of("library-links.find.title-missing");

    private static readonly Message PasteInvalidError = Message.Of("library-links.find.paste-invalid");

    private static readonly Message BareNumberKindError = Message.Of("library-links.find.bare-number-kind");

    /// <summary>
    /// R21 — an IMDb id TMDb files no film or show under. The catalog contract reads only a find's films and shows, so an
    /// episode's id — the documentaries' usual case — and an unknown one look alike; the sentence names the likely fix.
    /// </summary>
    private static readonly Message ImdbNotFoundError = Message.Of("library-links.find.imdb-not-found");

    /// <summary>The years TMDb's search takes (the TMDb adapter's rule).</summary>
    private const int MinimumYear = 1;

    private const int MaximumYear = 9999;

    // The release search's own words for the same mistake (ReleaseSearchRules): one key per wording (MAM-47, ADR 0042).
    private static Message YearInvalidError(string year) => Message.Of("releases.search.year-invalid", ("value", Echo(year)));

    public async Task<LinkCandidateListResult> SearchAsync(Guid workId, string? title, string? year, CancellationToken cancellationToken = default)
    {
        var work = await db.MediaWorks.AsNoTracking().Where(row => row.Id == workId).Select(row => new { row.Kind }).SingleOrDefaultAsync(cancellationToken);
        if (work is null)
            return LinkCandidateListResult.NotFound;
        var text = title?.Trim();
        if (string.IsNullOrEmpty(text))
            return LinkCandidateListResult.Invalid(TitleMissingError);
        int? searchedYear = null;
        if (!string.IsNullOrWhiteSpace(year))
        {
            if (!int.TryParse(year.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < MinimumYear or > MaximumYear)
                return LinkCandidateListResult.Invalid(YearInvalidError(year.Trim()));
            searchedYear = parsed;
        }

        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
            return LinkCandidateListResult.Conflict(connected.Error!);
        if (await GenresFailureAsync(catalog, cancellationToken) is { } genresFailure)
            return genresFailure;

        // The kinds of catalog work the work could be linked to: a film may be a film or an episode of a show (decision
        // 15); a series or an episode only a show.
        MediaWorkKind[] kinds = work.Kind == MediaWorkKind.Movie ? [MediaWorkKind.Movie, MediaWorkKind.Series] : [MediaWorkKind.Series];
        var found = new List<CatalogWorkSummary>();
        foreach (var kind in kinds)
        {
            // The first page only, each result stored as a summary (its poster serves); the year narrows it.
            var answer = await store.SearchAsync(catalog, kind, text, searchedYear, cancellationToken);
            if (!answer.IsSuccess)
                return LinkCandidateListResult.BadGateway(Masked(answer, catalog));
            found.AddRange(answer.Value!.Where(summary => found.All(other => other.Kind != summary.Kind || other.Id != summary.Id)));
        }

        // The run's choice (LinkCandidateSelection): what the owner typed and the work's own titles count as exact.
        var titles = await db.MediaWorkTitles.AsNoTracking().Where(row => row.MediaWorkId == workId).ToListAsync(cancellationToken);
        var titleKeys = LibraryLinkWorkTitles.Cleaned(titles).Append(text).Select(TitleMatchKey.For).Where(key => key.Length > 0).ToHashSet(StringComparer.Ordinal);
        var chosen = LinkCandidateSelection.Choose(found, summary => summary, titleKeys, LibraryLinkSuggestionRunner.CandidatesPerWork);

        var keys = new List<LinkCandidateKey>();
        foreach (var summary in chosen)
        {
            var key = new LinkCandidateKey(summary.Kind, summary.Id);
            var details = await store.GetWorkAsync(catalog, key.Kind, key.CatalogId, cancellationToken);
            if (details.IsSuccess)
                keys.Add(key);
            else if (details.Status != IntegrationCallStatus.NotFound)
                return LinkCandidateListResult.BadGateway(Masked(details, catalog));
        }

        return LinkCandidateListResult.Listed(new LinkCandidateListResponse(await suggestions.RankAsync(workId, keys, cancellationToken: cancellationToken)));
    }

    public async Task<LinkCandidateListResult> LookupAsync(Guid workId, string? text, string? kind, CancellationToken cancellationToken = default)
    {
        if (!await db.MediaWorks.AnyAsync(row => row.Id == workId, cancellationToken))
            return LinkCandidateListResult.NotFound;
        if (IdentifierReferenceParser.Parse(text) is not { } reference)
            return LinkCandidateListResult.Invalid(PasteInvalidError);

        LinkCandidateKey? named = null;
        switch (reference.Scheme)
        {
            case null:
                // A bare number is in one of TMDb's two numberings; which one is the owner's to say (ADR 0017).
                var kindText = kind?.Trim();
                if (string.IsNullOrEmpty(kindText))
                    return LinkCandidateListResult.Invalid(BareNumberKindError);
                if (CatalogKindOf(kindText) is not { } bareKind)
                    return LinkCandidateListResult.Invalid(LibraryLinkService.KindInvalidError(kindText));
                named = new LinkCandidateKey(bareKind, reference.Value);
                break;
            case MediaIdentifierScheme.Tmdb:
                named = new LinkCandidateKey(MediaWorkKind.Movie, reference.Value);
                break;
            case MediaIdentifierScheme.TmdbTv:
                named = new LinkCandidateKey(MediaWorkKind.Series, reference.Value);
                break;
        }

        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
            return LinkCandidateListResult.Conflict(connected.Error!);
        if (await GenresFailureAsync(catalog, cancellationToken) is { } genresFailure)
            return genresFailure;

        List<LinkCandidateKey> keys;
        if (named is not null)
        {
            keys = [named];
        }
        else
        {
            // An IMDb id: what TMDb files under it, films first — a film and a show may share one.
            var found = await store.FindByImdbIdAsync(catalog, reference.Value, cancellationToken);
            if (!found.IsSuccess)
                return LinkCandidateListResult.BadGateway(Masked(found, catalog));
            keys = [.. found.Value!.Select(summary => new LinkCandidateKey(summary.Kind, summary.Id)).Distinct()];
        }

        var read = new List<LinkCandidateKey>();
        foreach (var key in keys)
        {
            // Through the cache: the film or show is stored, so its poster serves and the evidence reads its details.
            var details = await store.GetWorkAsync(catalog, key.Kind, key.CatalogId, cancellationToken);
            if (details.IsSuccess)
                read.Add(key);
            else if (details.Status != IntegrationCallStatus.NotFound)
                return LinkCandidateListResult.BadGateway(Masked(details, catalog));
        }

        // R18 — an id TMDb doesn't have is a 404 with the sentence.
        if (read.Count == 0)
            return LinkCandidateListResult.CatalogWorkNotFound(
                named is null ? ImdbNotFoundError : LibraryLinkService.CatalogWorkNotFoundError(named.Kind, named.CatalogId));

        var ranked = await suggestions.RankAsync(workId, read, reference.SeasonNumber, reference.EpisodeNumber, cancellationToken);
        return LinkCandidateListResult.Listed(new LinkCandidateListResponse(ranked));
    }

    /// <summary>
    /// Reads the catalog's genre lists through the cache (a call or two a month), so the candidates' genres have roles
    /// for their evidence; the failure as the owner reads it, or <c>null</c>.
    /// </summary>
    private async Task<LinkCandidateListResult?> GenresFailureAsync(CatalogConnection catalog, CancellationToken cancellationToken)
    {
        foreach (var kind in (MediaWorkKind[])[MediaWorkKind.Movie, MediaWorkKind.Series])
        {
            var genres = await store.GetGenresAsync(catalog, kind, cancellationToken);
            if (!genres.IsSuccess)
                return LinkCandidateListResult.BadGateway(Masked(genres, catalog));
        }

        return null;
    }
}
