using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Health;

/// <summary>
/// Ready means the database opens and its schema is the one this build expects — a database that
/// opens but misses a migration would fail the first query that touches the missing part.
/// </summary>
/// <remarks>
/// MAM-47 (ADR 0042) — the result's sentence is a message under <see cref="HealthResponseWriter.MessageKey"/> in its
/// data: the writer renders it in the language of the request that asked, and a check result has no request to know it
/// from. Its English is the description all the same, because the health check service logs every result with its
/// description ("… with message '…'", at Error when unhealthy) — the operator's one record of why readiness failed,
/// which stays in English as every log line does.
/// </remarks>
public sealed class DatabaseHealthCheck(ApplicationDbContext db, IRequestTexts texts, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public const string Name = "database";

    private static readonly Message CannotOpen = Message.Of("common.health.database-cannot-open");

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return Result(HealthStatus.Unhealthy, CannotOpen);

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            return pending == 0
                ? Result(HealthStatus.Healthy, Message.Of("common.health.database-ready"))
                : Result(HealthStatus.Unhealthy, Message.Of("common.health.pending-migrations", ("count", pending)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Caught here rather than left to the health-check service, so the answer says what
            // failed ("cannot be opened") instead of the response writer's generic sentence for a
            // check that throws. The detail (file paths, SQLite internals) goes to the log; Warning,
            // because a failing readiness probe needs attention.
            logger.LogWarning(exception, "The database readiness check failed.");
            return Result(HealthStatus.Unhealthy, CannotOpen);
        }
    }

    private HealthCheckResult Result(HealthStatus status, Message message) =>
        new(status, texts.English(message), data: new Dictionary<string, object> { [HealthResponseWriter.MessageKey] = message });
}
