using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Health;

// MAM-47 (ADR 0042) — the readiness sentence counts its migrations with the plural forms of common.health.pending-
// migrations: HealthReadinessFailureHttpTests pins the one, this the other. Its own class, and so its own factory and
// database: it breaks the migration history, which ResetAsync does not restore, by more than the one migration the
// other class expects pending.
public sealed class HealthPendingMigrationsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string ReadyUrl = "/api/health/ready";

    // The framework's internal DefaultHealthCheckService, which runs the checks and logs each result.
    private const string HealthCheckServiceCategory = "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService";

    private readonly MediaAcquisitionManagerApiFactory _factory;

    public HealthPendingMigrationsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Readiness_with_two_pending_migrations_is_503_and_counts_them_in_the_answer_and_the_english_log()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The two newest migrations the app knows, by name: the same two rows whichever order the runs come in.
        await _factory.WithDbAsync(db =>
        {
            var newest = db.Database.GetMigrations().TakeLast(2).ToList();
            return db.Database.ExecuteSqlAsync(
                $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {newest[0]} OR \"MigrationId\" = {newest[1]};",
                cancellationToken);
        });

        //Act
        var response = await _factory.GetHttpRequest(ReadyUrl);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Unhealthy, body!.Status);
        Assert.Equal(
            new HealthCheckDto("database", HealthStatus.Unhealthy, "The database has 2 pending migrations."),
            Assert.Single(body.Checks));
        // The health check service's own Error line quotes the check's description: the operator's one record of why
        // readiness failed, in English as every log line (ADR 0042), whatever language the probe asked in.
        var logged = Assert.Single(
            _factory.Logs.Entries, entry => entry.Category == HealthCheckServiceCategory && entry.Level == LogLevel.Error);
        Assert.EndsWith(" with message 'The database has 2 pending migrations.'", logged.Message, StringComparison.Ordinal);
    }
}
