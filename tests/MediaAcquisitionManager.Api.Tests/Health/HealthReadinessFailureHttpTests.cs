using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Tests.Health;

// Its own class, and so its own factories and databases: the tests here break the migration history,
// which ResetAsync does not restore (it only empties the model's tables), or run with a readiness
// check that throws.
public sealed class HealthReadinessFailureHttpTests
    : IClassFixture<MediaAcquisitionManagerApiFactory>, IClassFixture<ThrowingHealthCheckApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/health";
    private const string ReadyUrl = BaseUrl + "/ready";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private readonly ThrowingHealthCheckApiFactory _throwingCheck;

    public HealthReadinessFailureHttpTests(MediaAcquisitionManagerApiFactory factory, ThrowingHealthCheckApiFactory throwingCheck)
    {
        _factory = factory;
        _throwingCheck = throwingCheck;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        await _throwingCheck.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Readiness_with_a_pending_migration_is_503_unhealthy_and_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ForgetTheNewestAppliedMigrationAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(ReadyUrl);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Unhealthy, body!.Status);
        var check = Assert.Single(body.Checks);
        Assert.Equal(new HealthCheckDto(
            "database", HealthStatus.Unhealthy, "The database has 1 pending migration."), check);
    }

    [Fact]
    public async Task Liveness_with_a_pending_migration_is_still_200()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await ForgetTheNewestAppliedMigrationAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_with_a_check_that_throws_is_503_without_the_exception_text()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _throwingCheck.GetHttpRequest(ReadyUrl);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(ThrowingHealthCheck.ThrownMessage, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.5", raw, StringComparison.Ordinal);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Unhealthy, body!.Status);
        Assert.Equal(
            new HealthCheckDto(ThrowingHealthCheck.Name, HealthStatus.Unhealthy, "The probe check failed; see the server log."),
            Assert.Single(body.Checks, check => check.Name == ThrowingHealthCheck.Name));
        Assert.Equal(
            new HealthCheckDto("database", HealthStatus.Healthy, "The database is reachable and every migration is applied."),
            Assert.Single(body.Checks, check => check.Name == "database"));
    }

    // The app migrated on startup; deleting the newest history row makes that one migration pending
    // again without touching the schema — the state an older build's database is in. Only the newest,
    // so the expected count stays 1 however many migrations the app gains.
    private Task ForgetTheNewestAppliedMigrationAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = (SELECT MAX(\"MigrationId\") FROM \"__EFMigrationsHistory\");",
            cancellationToken));
}
