using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Health;

/// <summary>
/// Ready means the database opens and its schema is the one this build expects — a database that
/// opens but misses a migration would fail the first query that touches the missing part.
/// </summary>
public sealed class DatabaseHealthCheck(ApplicationDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public const string Name = "database";

    private const string CannotOpen = "The database cannot be opened.";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy(CannotOpen);

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            return pending switch
            {
                0 => HealthCheckResult.Healthy("The database is reachable and every migration is applied."),
                1 => HealthCheckResult.Unhealthy("The database has 1 pending migration."),
                _ => HealthCheckResult.Unhealthy($"The database has {pending} pending migrations."),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Caught here rather than left to the health-check service, so the answer says what
            // failed ("cannot be opened") instead of the response writer's generic sentence for a
            // check that throws. The detail (file paths, SQLite internals) goes to the log; Warning,
            // because a failing readiness probe needs attention.
            logger.LogWarning(exception, "The database readiness check failed.");
            return HealthCheckResult.Unhealthy(CannotOpen);
        }
    }
}
