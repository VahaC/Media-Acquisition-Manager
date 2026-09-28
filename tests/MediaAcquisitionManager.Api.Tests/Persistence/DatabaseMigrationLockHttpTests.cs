using System.Net;
using MediaAcquisitionManager.Api.Extensions;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Persistence;

// On AbandonedMigrationLockApiFactory: the database the app starts on holds a migration lock that a
// killed process left behind.
public sealed class DatabaseMigrationLockHttpTests : IClassFixture<AbandonedMigrationLockApiFactory>, IAsyncLifetime
{
    private const string ReadyUrl = "/api/health/ready";

    /// <summary>
    /// Far beyond a normal start (a second or two, more on a busy CI agent). Without the fix the
    /// start never ends, and the test has to fail rather than hang the run.
    /// </summary>
    private static readonly TimeSpan StartupDeadline = TimeSpan.FromSeconds(60);

    private readonly AbandonedMigrationLockApiFactory _factory;

    public DatabaseMigrationLockHttpTests(AbandonedMigrationLockApiFactory factory) => _factory = factory;

    // No ResetAsync here, on purpose: it starts the host, and the start is what the test runs under a
    // deadline. A reset would also empty the logs the start wrote.
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Startup_with_an_abandoned_migration_lock_removes_it_and_becomes_ready()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var lockRows = -1;

        //Act
        // On the thread pool, because a start that hangs blocks its thread for good.
        var start = Task.Run(() => _factory.Services, cancellationToken);
        var startedInTime = await Task.WhenAny(start, Task.Delay(StartupDeadline, cancellationToken)) == start;
        var response = startedInTime ? await _factory.GetHttpRequest(ReadyUrl) : null;
        if (startedInTime)
        {
            await _factory.WithDbAsync(async db => lockRows = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"__EFMigrationsLock\"")
                .SingleAsync(cancellationToken));
        }

        //Assert
        Assert.True(startedInTime, $"The host did not start within {StartupDeadline}: Migrate() is still waiting for the lock.");
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
        Assert.Equal(0, lockRows);
        var warning = Assert.Single(
            _factory.Logs.Entries,
            entry => entry.Category == typeof(DatabaseMigrationExtensions).FullName && entry.Level == LogLevel.Warning);
        Assert.Contains(AbandonedMigrationLockApiFactory.LockTakenAt, warning.Message, StringComparison.Ordinal);
    }
}
