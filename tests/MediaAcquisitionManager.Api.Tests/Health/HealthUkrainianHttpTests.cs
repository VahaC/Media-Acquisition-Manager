using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Tests.Health;

// MAM-47 WP14 (ADR 0042, design §4.6, §8.6) — the readiness probe in Ukrainian: each check's description follows the
// request's language — the healthy database, the pending migrations counted in the noun's own form (1 міграцію, 2
// міграції), a check that threw named in «…» — and the same probe without the header answers English. Its own class, and
// so its own databases: a test here breaks the migration history, which ResetAsync doesn't restore, so it puts the rows
// back after itself. Each answer spelled here, never read from the locale files.
public sealed class HealthUkrainianHttpTests
    : IClassFixture<MediaAcquisitionManagerApiFactory>, IClassFixture<ThrowingHealthCheckApiFactory>, IAsyncLifetime
{
    private const string ReadyUrl = "/api/health/ready";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private readonly ThrowingHealthCheckApiFactory _throwingCheck;
    private readonly List<(string MigrationId, string ProductVersion)> _forgotten = [];

    public HealthUkrainianHttpTests(MediaAcquisitionManagerApiFactory factory, ThrowingHealthCheckApiFactory throwingCheck)
    {
        _factory = factory;
        _throwingCheck = throwingCheck;
    }

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        await _throwingCheck.ResetAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // The history rows a test forgot, back as they were: the next test meets every migration applied.
        foreach (var (migrationId, productVersion) in _forgotten)
        {
            await _factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
                $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {productVersion});"));
        }
    }

    [Fact]
    public async Task Readiness_asked_in_ukrainian_describes_the_healthy_database_in_ukrainian()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var ukrainian = await ReadyAsync(_factory, "uk", cancellationToken);
        var english = await ReadyAsync(_factory, null, cancellationToken);

        //Assert
        Assert.Equal(
            (HttpStatusCode.OK, new HealthCheckDto("database", HealthStatus.Healthy, "База даних доступна, і всі міграції застосовано.")),
            (ukrainian.Status, Assert.Single(ukrainian.Body.Checks)));
        Assert.Equal(
            (HttpStatusCode.OK, new HealthCheckDto("database", HealthStatus.Healthy, "The database is reachable and every migration is applied.")),
            (english.Status, Assert.Single(english.Body.Checks)));
    }

    // MAM-48 F5 — the probe is written outside MVC, so it once kept the serializer's default encoder and sent every Cyrillic
    // letter as a \uXXXX escape while the controllers sent raw UTF-8. Read as bytes, not through a JSON reader that would
    // hide the escapes; English, which has nothing to escape, keeps the very bytes it always had.
    [Fact]
    public async Task Readiness_writes_the_ukrainian_description_as_raw_utf8_and_the_english_unchanged()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var ukrainian = await ReadyBytesAsync("uk", cancellationToken);
        var english = await ReadyBytesAsync("en", cancellationToken);

        //Assert
        Assert.Equal(("utf-8", "utf-8"), (ukrainian.CharSet, english.CharSet));
        Assert.Equal(
            """{"status":"Healthy","checks":[{"name":"database","status":"Healthy","description":"База даних доступна, і всі міграції застосовано."}]}""",
            Encoding.UTF8.GetString(ukrainian.Body));
        Assert.Equal(
            """{"status":"Healthy","checks":[{"name":"database","status":"Healthy","description":"The database is reachable and every migration is applied."}]}""",
            Encoding.UTF8.GetString(english.Body));
    }

    [Fact]
    public async Task Readiness_asked_in_ukrainian_counts_the_pending_migrations_in_their_own_form()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await ForgetTheNewestAppliedMigrationAsync(cancellationToken);
        var one = await ReadyAsync(_factory, "uk", cancellationToken);
        await ForgetTheNewestAppliedMigrationAsync(cancellationToken);
        var two = await ReadyAsync(_factory, "uk", cancellationToken);

        //Assert
        Assert.Equal(
            (HttpStatusCode.ServiceUnavailable, new HealthCheckDto("database", HealthStatus.Unhealthy, "У базі даних не застосовано 1 міграцію.")),
            (one.Status, Assert.Single(one.Body.Checks)));
        Assert.Equal(
            (HttpStatusCode.ServiceUnavailable, new HealthCheckDto("database", HealthStatus.Unhealthy, "У базі даних не застосовано 2 міграції.")),
            (two.Status, Assert.Single(two.Body.Checks)));
    }

    [Fact]
    public async Task Readiness_with_a_check_that_throws_asked_in_ukrainian_names_the_check_and_not_the_exception()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var answer = await ReadyAsync(_throwingCheck, "uk", cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.Status);
        Assert.Equal(
            new HealthCheckDto(ThrowingHealthCheck.Name, HealthStatus.Unhealthy, "Перевірку «probe» не пройдено; подробиці — в журналі сервера."),
            Assert.Single(answer.Body.Checks, check => check.Name == ThrowingHealthCheck.Name));
    }

    private static async Task<(HttpStatusCode Status, HealthResponse Body)> ReadyAsync(
        MediaAcquisitionManagerApiFactory factory, string? language, CancellationToken cancellationToken)
    {
        var response = await factory.GetHttpRequest(ReadyUrl, language: language);
        // A probe asked in a language reads as it came; one asked in none reads through the pass's own checks.
        var options = language is null ? MediaAcquisitionManagerApiFactory.JsonOptions : MediaAcquisitionManagerApiFactory.PlainJsonOptions;
        return (response.StatusCode, (await response.Content.ReadFromJsonAsync<HealthResponse>(options, cancellationToken))!);
    }

    private async Task<(string? CharSet, byte[] Body)> ReadyBytesAsync(string language, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest(ReadyUrl, language: language);
        return (response.Content.Headers.ContentType?.CharSet, await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    // The newest migration still in the history, forgotten: one more migration pending, the schema untouched — the state
    // an older build's database is in. Remembered, so DisposeAsync puts it back.
    private Task ForgetTheNewestAppliedMigrationAsync(CancellationToken cancellationToken) =>
        _factory.WithDbAsync(async db =>
        {
            var migrationId = await db.Database
                .SqlQuery<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1")
                .SingleAsync(cancellationToken);
            var productVersion = await db.Database
                .SqlQuery<string>($"SELECT \"ProductVersion\" AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {migrationId}")
                .SingleAsync(cancellationToken);
            await db.Database.ExecuteSqlAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {migrationId};", cancellationToken);
            _forgotten.Add((migrationId, productVersion));
        });
}
