using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationDismissalService"/>
public sealed class RecommendationDismissalService(
    ApplicationDbContext db,
    TimeProvider time,
    ILogger<RecommendationDismissalService> logger) : IRecommendationDismissalService
{
    public async Task<RecommendationDismissalResult> DismissAsync(
        string? kind, string? catalogId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (RecommendationDismissalRules.Validate(kind, catalogId, out var workKind, out var workCatalogId) is { } error)
            return RecommendationDismissalResult.Invalid(error);
        if (await ExistsAsync(userId, workKind!.Value, workCatalogId!, cancellationToken))
            return RecommendationDismissalResult.Done;

        db.RecommendationDismissals.Add(new RecommendationDismissalEntity
        {
            UserId = userId,
            Kind = workKind.Value,
            CatalogId = workCatalogId!,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two clicks can both find no mark; the unique index (user, kind, id) lets one commit, and this one lost —
            // which is what was asked for anyway. Anything else (a full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (!await ExistsAsync(userId, workKind.Value, workCatalogId!, cancellationToken))
                throw;
            logger.LogInformation(
                exception, "A concurrent request marked {Kind} {CatalogId} \"Not interested\" for user {UserId} first.", workKind.Value, workCatalogId, userId);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return RecommendationDismissalResult.Done;
    }

    public async Task<RecommendationDismissalResult> UndismissAsync(
        string? kind, string? catalogId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (RecommendationDismissalRules.Validate(kind, catalogId, out var workKind, out var workCatalogId) is { } error)
            return RecommendationDismissalResult.Invalid(error);

        // The caller's own mark only, by the user in the query: another user's is never touched.
        var markedKind = workKind!.Value;
        await db.RecommendationDismissals
            .Where(dismissal => dismissal.UserId == userId && dismissal.Kind == markedKind && dismissal.CatalogId == workCatalogId)
            .ExecuteDeleteAsync(cancellationToken);
        return RecommendationDismissalResult.Done;
    }

    private Task<bool> ExistsAsync(Guid userId, MediaWorkKind kind, string catalogId, CancellationToken cancellationToken) =>
        db.RecommendationDismissals.AnyAsync(
            dismissal => dismissal.UserId == userId && dismissal.Kind == kind && dismissal.CatalogId == catalogId, cancellationToken);
}
