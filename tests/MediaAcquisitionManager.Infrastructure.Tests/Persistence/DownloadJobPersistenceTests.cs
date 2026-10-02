using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-13 — the download job's promises, pinned on the migrated schema: one job per request a user sent,
// whatever two copies of that request do at once (ADR 0013); request ids that are another user's own, and
// one user's many requests, side by side; and a job that outlives the user who sent it, still naming them.
public sealed class DownloadJobPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    private static readonly Guid RequestId = Guid.Parse("4f1d2c3b-5a69-4e7d-8c0b-1a2b3c4d5e6f");

    public DownloadJobPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task A_second_job_for_one_users_request_id_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var release = await Releases(db).Create(sourceConnectionId: null);
        await Jobs(db).Create(release.Id, null, "qBittorrent", requestedByUserId: user.Id, requestId: RequestId);

        //Act
        var exception = await Record.ExceptionAsync(() =>
            Jobs(db).Create(release.Id, null, "qBittorrent", requestedByUserId: user.Id, requestId: RequestId));

        //Assert
        // The backstop behind the service, which answers a repeated request with the job it made.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: DownloadJobs.RequestedByUserId, DownloadJobs.RequestId", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.DownloadJobs.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Two_users_may_send_under_one_request_id_and_one_user_under_many()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var vasyl = await CreateUserAsync(db, "vasyl");
        var member = await CreateUserAsync(db, "member");
        var release = await Releases(db).Create(sourceConnectionId: null);

        //Act
        await Jobs(db).Create(release.Id, null, "qBittorrent", requestedByUserId: vasyl.Id, requestId: RequestId);
        await Jobs(db).Create(release.Id, null, "qBittorrent", requestedByUserId: member.Id, requestId: RequestId);
        await Jobs(db).Create(release.Id, null, "qBittorrent", requestedByUserId: vasyl.Id, requestId: Guid.NewGuid());

        //Assert
        await using var check = CreateContext();
        Assert.Equal(
            ["member", "vasyl", "vasyl"],
            (await check.DownloadJobs.Select(job => job.RequestedByUserName!).ToListAsync(cancellationToken)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Deleting_a_user_keeps_their_jobs_with_their_name_and_frees_their_request_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var user = await CreateUserAsync(db, "vasyl");
        var release = await Releases(db).Create(sourceConnectionId: null);
        var job = await Jobs(db).Create(release.Id, null, "qBittorrent", requestedByUserId: user.Id, requestId: RequestId);

        //Act
        await db.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync(cancellationToken);
        // Another job of no user, under the same request id: SQLite counts no two nulls the same.
        await Jobs(db).Create(release.Id, null, "qBittorrent", requestId: RequestId);

        //Assert
        // SetNull: the job is history and outlives the user; the copied name keeps who sent it.
        var stored = await db.DownloadJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, cancellationToken);
        Assert.Null(stored.RequestedByUserId);
        Assert.Equal("vasyl", stored.RequestedByUserName);
        Assert.Equal(RequestId, stored.RequestId);
        Assert.Equal(2, await db.DownloadJobs.CountAsync(x => x.RequestId == RequestId, cancellationToken));
    }

    [Fact]
    public async Task A_job_keeps_both_info_hashes_of_a_hybrid_torrent_whole()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        const string v1 = "aaeeecd05bc4058a5dd980de3cd8c7c1794034bc";
        const string v2 = "b29a07ddcde6736c607836fc557abceaaaf64a860607c374fbc2d9de02ffc274";

        //Act
        var job = await Jobs(db).Create(release.Id, null, "qBittorrent", status: DownloadJobStatus.Submitted, infoHashV1: v1, infoHashV2: v2);

        //Assert
        // Both, and the v2 hash at its full 64 characters: a client may name the torrent by either.
        var stored = await db.DownloadJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, cancellationToken);
        Assert.Equal((v1, v2), (stored.InfoHashV1, stored.InfoHashV2));
    }

    [Fact]
    public async Task The_jobs_lookups_have_their_indexes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var indexes = await db.Database
            .SqlQuery<string>($"SELECT \"name\" AS \"Value\" FROM pragma_index_list('DownloadJobs') WHERE \"origin\" = 'c'")
            .ToListAsync(cancellationToken);

        //Assert
        // A user's request (unique, the idempotency key's), and each foreign key's own.
        Assert.Equal(
            [
                "IX_DownloadJobs_DestinationMappingId",
                "IX_DownloadJobs_DownloadClientConnectionId",
                "IX_DownloadJobs_ReleaseId",
                "IX_DownloadJobs_RequestedByUserId_RequestId",
            ],
            indexes.Order(StringComparer.Ordinal));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");

    private static IReleaseDataFactory Releases(ApplicationDbContext db) => new ReleaseDataFactory(db, Encryption, TimeProvider.System);

    private static IDownloadJobDataFactory Jobs(ApplicationDbContext db) => new DownloadJobDataFactory(db, TimeProvider.System);
}
