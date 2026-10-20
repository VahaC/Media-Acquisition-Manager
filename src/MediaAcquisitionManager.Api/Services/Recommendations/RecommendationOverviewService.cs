using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationOverviewService"/>
public sealed class RecommendationOverviewService(
    ApplicationDbContext db,
    ICatalogConnector catalogs,
    TimeProvider time,
    ILogger<RecommendationOverviewService> logger) : IRecommendationOverviewService
{
    /// <summary>
    /// What a failed request tells anyone but the installation owner. The catalog's own sentence can quote the address
    /// the owner saved — the rule of a run's error, a download job's and a poster's. MAM-47 — a message under the name
    /// the constant had (ADR 0042).
    /// </summary>
    public static readonly Message FetchFailedError = Message.Of("recommendations.overview.fetch-failed");

    public async Task<RecommendationOverviewResult> GetAsync(
        string? kind, string? catalogId, string? language, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (RecommendationDismissalRules.Validate(kind, catalogId, out var workKind, out var workCatalogId) is { } workError)
            return RecommendationOverviewResult.Invalid(workError);
        if (RecommendationOverviewRules.ValidateLanguage(language, out var asked) is { } languageError)
            return RecommendationOverviewResult.Invalid(languageError);

        var work = await db.CatalogWorks.AsNoTracking()
            .Where(row => row.Kind == workKind!.Value && row.CatalogId == workCatalogId)
            .Select(row => new WorkRef(row.Id, row.Kind, row.CatalogId))
            .SingleOrDefaultAsync(cancellationToken);
        if (work is null)
            return RecommendationOverviewResult.NotFound;

        // Connected once, by the first language that has to ask.
        CatalogConnectResult? connected = null;
        async Task<CatalogConnectResult> ConnectOnceAsync() => connected ??= await catalogs.ConnectAsync(cancellationToken);

        // The language asked, then English — the one every user of the app reads — when the catalog has none in it.
        string[] languages = RecommendationOverviewRules.IsFallback(asked!) ? [asked!] : [asked!, RecommendationOverviewRules.FallbackLanguage];
        foreach (var candidate in languages)
        {
            var answer = await ReadAsync(work, candidate, ConnectOnceAsync, callerUserId, cancellationToken);
            if (answer.Status != RecommendationOverviewStatus.Found || answer.Overview!.Overview is not null)
                return answer;
        }

        return RecommendationOverviewResult.Found(null, null);
    }

    /// <summary>The overview of <paramref name="work"/> in <paramref name="language"/>: kept while fresh, else asked of the
    /// catalog and kept — a <see cref="RecommendationOverviewStatus.Found"/> answer without one when it has none in it.
    /// A kept answer, however old, beats none while the catalog can't be asked.</summary>
    private async Task<RecommendationOverviewResult> ReadAsync(
        WorkRef work, string language, Func<Task<CatalogConnectResult>> connect, Guid callerUserId, CancellationToken cancellationToken)
    {
        var kept = await db.CatalogWorkOverviews.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CatalogWorkId == work.Id && row.Language == language, cancellationToken);
        if (kept is not null && kept.FetchedUtc >= time.GetUtcNow().UtcDateTime - CatalogStore.RefreshAge)
            return Found(kept.Overview, language);

        var connected = await connect();
        if (connected.Connection is not { } catalog)
        {
            if (kept is not null)
                return Found(kept.Overview, language);
            return connected.Status == CatalogConnectStatus.NoCatalog
                ? RecommendationOverviewResult.NoCatalog(connected.Error!)
                : await FailedAsync(connected.Error!, callerUserId, cancellationToken);
        }

        var answer = await catalog.Catalog.GetOverviewAsync(catalog.Settings, work.Kind, work.CatalogId, language, cancellationToken);
        if (!answer.IsSuccess && answer.Status != IntegrationCallStatus.NotFound)
        {
            if (kept is not null)
                return Found(kept.Overview, language);
            // Masked again with the call's secrets — a second pass after the adapter's own (the MAM-7 rule), over the
            // message's values (ADR 0042).
            return await FailedAsync(answer.Error!.Redacted(catalog.Settings.Secrets), callerUserId, cancellationToken);
        }

        // A work the catalog no longer knows has nothing to say about it, and that answer is kept like any other.
        var overview = answer.IsSuccess ? answer.Value!.Text : null;
        await KeepAsync(work, kept?.Id, language, overview, cancellationToken);
        return Found(overview, language);
    }

    private static RecommendationOverviewResult Found(string? overview, string language) =>
        RecommendationOverviewResult.Found(overview, overview is null ? null : language);

    private async Task KeepAsync(WorkRef work, Guid? keptId, string language, string? overview, CancellationToken cancellationToken)
    {
        var fetchedUtc = time.GetUtcNow().UtcDateTime;
        if (keptId is { } id)
        {
            await db.CatalogWorkOverviews
                .Where(row => row.Id == id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Overview, overview).SetProperty(row => row.FetchedUtc, fetchedUtc),
                    cancellationToken);
            return;
        }

        db.CatalogWorkOverviews.Add(new CatalogWorkOverviewEntity
        {
            CatalogWorkId = work.Id,
            Language = language,
            Overview = overview,
            FetchedUtc = fetchedUtc,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Two first clicks can both find nothing kept; the unique index (work, language) lets one commit, and this one
            // lost — with the same answer. Anything else (a full disk) stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.CatalogWorkOverviews.AnyAsync(row => row.CatalogWorkId == work.Id && row.Language == language, cancellationToken))
                throw;
            logger.LogInformation(
                exception, "A concurrent request kept the {Language} overview of {Kind} {CatalogId} first.", language, work.Kind, work.CatalogId);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>A failed request: <paramref name="error"/> for the installation owner, <see cref="FetchFailedError"/> for
    /// everyone else. Information, not Warning, as a poster's: the next click asks again.</summary>
    private async Task<RecommendationOverviewResult> FailedAsync(Message error, Guid callerUserId, CancellationToken cancellationToken)
    {
        logger.LogInformation("A description couldn't be fetched from the metadata catalog.");
        return RecommendationOverviewResult.Failed(
            await db.IsInstallationOwnerAsync(callerUserId, cancellationToken) ? error : FetchFailedError);
    }

    /// <summary>The work asked about: its row and how the catalog numbers it.</summary>
    private sealed record WorkRef(Guid Id, MediaWorkKind Kind, string CatalogId);
}
