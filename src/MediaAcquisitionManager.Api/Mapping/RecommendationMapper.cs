using System.Globalization;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Mapping;

/// <inheritdoc cref="IRecommendationMapper"/>
public sealed class RecommendationMapper : IRecommendationMapper
{
    public RecommendationRunDto ToDto(RecommendationRunEntity run, bool forOwner) =>
        new(
            run.Id,
            run.Status,
            // MAM-33 — every signed-in user sees what a run is doing, as they see its status: no address or secret in it.
            run.Stage,
            run.StageDone,
            run.StageTotal,
            run.Trigger,
            // The sync run's rule: who asked is the owner's sign-in name, and only the owner sees it.
            forOwner ? run.RequestedByUserName : null,
            run.CreatedUtc,
            run.StartedUtc,
            run.FinishedUtc,
            run.SeedCount,
            run.CandidateCount,
            // The catalog's sentence can quote its address or what it said back about the key.
            forOwner ? run.Error : null);

    public RecommendationDto ToDto(
        CatalogWorkEntity work,
        IReadOnlyList<string> genres,
        IReadOnlyList<RecommendationReasonEntity> reasons,
        ReleaseAvailabilityEntity? availability,
        bool downloading)
    {
        var year = work.ReleaseDate?.Year;
        // The query title the availability check sends (RecommendationQueryTitle), so "Releases" searches for what
        // the check found; the year in brackets is how the Search page reads a year out of its text.
        var queryTitle = RecommendationQueryTitle.For(work.Title, work.OriginalTitle);
        return new RecommendationDto(
            work.Id,
            work.Kind,
            work.CatalogId,
            work.Title,
            work.OriginalTitle,
            year,
            genres,
            work.Rating,
            work.VoteCount,
            // Only for a name the posters route serves, so a card never asks for one that can only 404.
            CatalogPosterUrls.Of(work.PosterPath),
            [.. reasons.Select(ReasonDto)],
            availability is null
                ? null
                : new RecommendationAvailabilityDto(
                    availability.CheckedUtc,
                    availability.Found,
                    availability.ReleaseCount,
                    availability.BestResolution,
                    availability.HasUkrainianAudio,
                    availability.MaxSeeders),
            downloading,
            year is { } known ? string.Create(CultureInfo.InvariantCulture, $"{queryTitle} ({known})") : queryTitle,
            // A film's id only: the Search page sends it as a movie's TMDb id (ADR 0017 numbers shows apart).
            work.Kind == MediaWorkKind.Movie ? work.CatalogId : null);
    }

    public RecommendationSettingsDto ToDto(RecommendationSettings settings, int searchesLast24Hours) =>
        new(settings.Movies, settings.Series, settings.Cartoons, settings.Documentaries, settings.DailySearchLimit, searchesLast24Hours);

    /// <summary>A reason row as its chip reads it: the stored lists as arrays, never the stored strings.</summary>
    private static RecommendationReasonDto ReasonDto(RecommendationReasonEntity reason) =>
        new(
            reason.Kind,
            reason.Name,
            reason.Held,
            reason.Total,
            // The run joins them with '\n' and stores "" for none.
            [.. (reason.Titles ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)],
            // The run writes them "4,5"; a part that isn't a number names no season.
            [
                .. (reason.Seasons ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var season) ? season : (int?)null)
                    .OfType<int>(),
            ],
            reason.DateUtc);
}
