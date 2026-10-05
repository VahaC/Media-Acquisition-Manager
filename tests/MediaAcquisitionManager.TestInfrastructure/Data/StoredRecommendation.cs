using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// MAM-33 — one stored recommendation candidate with its catalog work and its reasons, read at once, so a test asserts
/// what a run stored row by row.
/// </summary>
/// <param name="Kind">The catalog work's kind.</param>
/// <param name="CatalogId">The catalog work's id.</param>
/// <param name="Title">The catalog work's title.</param>
/// <param name="Tab">The tab it is in.</param>
/// <param name="Score">Its score.</param>
/// <param name="RequiresAvailability">Whether it is listed only once a source carries it.</param>
/// <param name="MissingSeasons">A held series' missing seasons.</param>
/// <param name="Reasons">Its reasons, by position.</param>
public sealed record StoredRecommendation(
    MediaWorkKind Kind,
    string CatalogId,
    string Title,
    RecommendationTab Tab,
    double Score,
    bool RequiresAvailability,
    string? MissingSeasons,
    IReadOnlyList<StoredRecommendationReason> Reasons)
{
    /// <summary>Every stored candidate in <paramref name="db"/>, by kind, then catalog id as a number would sort (the
    /// catalog's ids are numbers), each with its reasons by position.</summary>
    public static async Task<IReadOnlyList<StoredRecommendation>> ReadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var candidates = await (
                from candidate in db.RecommendationCandidates.AsNoTracking()
                join work in db.CatalogWorks.AsNoTracking() on candidate.CatalogWorkId equals work.Id
                select new { candidate, work })
            .ToListAsync(cancellationToken);
        var reasons = (await db.RecommendationReasons.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(reason => reason.RecommendationCandidateId);
        return
        [
            .. candidates
                .OrderBy(row => row.work.Kind)
                .ThenBy(row => row.work.CatalogId.Length)
                .ThenBy(row => row.work.CatalogId, StringComparer.Ordinal)
                .Select(row => new StoredRecommendation(
                    row.work.Kind,
                    row.work.CatalogId,
                    row.work.Title,
                    row.candidate.Tab,
                    row.candidate.Score,
                    row.candidate.RequiresAvailability,
                    row.candidate.MissingSeasons,
                    [
                        .. reasons[row.candidate.Id]
                            .OrderBy(reason => reason.Position)
                            .Select(reason => new StoredRecommendationReason(
                                reason.Kind, reason.Name, reason.Held, reason.Total, reason.Titles, reason.Seasons, reason.DateUtc)),
                    ])),
        ];
    }
}
