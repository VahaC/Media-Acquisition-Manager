using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="ILibraryLinkSuggestionMapper"/>
public sealed class LibraryLinkSuggestionMapper : ILibraryLinkSuggestionMapper
{
    public LinkCandidateDto ToCandidateDto(RankedCandidate ranked, CatalogWorkEntity work, int? seasonNumber, int? episodeNumber)
    {
        var candidate = ranked.Candidate;
        var evidence = ranked.Evidence;
        return new LinkCandidateDto(
            candidate.Kind,
            candidate.Id,
            candidate.Title,
            candidate.OriginalTitle,
            candidate.Year,
            work.Overview,
            candidate.RuntimeMinutes,
            // The recommendations' posters route: a suggestion is a stored catalog work, so its poster serves (MAM-33).
            CatalogPosterUrls.Of(work.PosterPath),
            work.ImdbId,
            ranked.IsConfident,
            new LinkEvidenceDto(
                new TitleEvidenceDto(evidence.Title.Similarity, evidence.Title.Exact, evidence.Title.MatchedKind, evidence.Title.MatchedTitle),
                new YearEvidenceDto(evidence.Year.LibraryYear, evidence.Year.CatalogYear, evidence.Year.Difference),
                new RuntimeEvidenceDto(evidence.Runtime.LibraryMinutes, evidence.Runtime.CatalogMinutes, evidence.Runtime.Agrees),
                evidence.KindAgrees,
                new GenreEvidenceDto(evidence.Genre.LibraryRole, evidence.Genre.Agreement),
                evidence.Sibling is { } sibling ? new SiblingEvidenceDto(sibling.Reason, sibling.SiblingTitle) : null),
            seasonNumber,
            episodeNumber);
    }

    public LibraryLinkSuggestionRunDto ToRunDto(LibraryLinkSuggestionRunEntity run) =>
        new(
            run.Id,
            run.Status,
            run.Trigger,
            // Owner-only endpoints (R19): who asked and why it failed — the catalog's sentence can quote its address — are
            // always in.
            run.RequestedByUserName,
            run.CreatedUtc,
            run.StartedUtc,
            run.FinishedUtc,
            run.WorksTotal,
            run.WorksDone,
            run.Error);
}
