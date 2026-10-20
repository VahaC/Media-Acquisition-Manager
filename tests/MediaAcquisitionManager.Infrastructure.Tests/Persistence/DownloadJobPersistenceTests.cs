using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
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
// MAM-14 — what following it keeps: its progress and why it isn't moving on, with whose sentence that is by
// name; the poller's index over the followed jobs only and the Jobs page's per user; and its files, one per place
// in the client's list, kept exactly and removed with the job.
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
        // A user's request (unique, the idempotency key's), each foreign key's own, and MAM-14's: the poller's
        // followed jobs, and a user's jobs newest first for the Jobs page.
        Assert.Equal(
            [
                "IX_DownloadJobs_DestinationMappingId",
                "IX_DownloadJobs_DownloadClientConnectionId",
                "IX_DownloadJobs_Followed",
                "IX_DownloadJobs_ReleaseId",
                "IX_DownloadJobs_RequestedByUserId_CreatedUtc",
                "IX_DownloadJobs_RequestedByUserId_RequestId",
            ],
            indexes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_followed_index_holds_only_the_jobs_still_followed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT \"sql\" AS \"Value\" FROM \"sqlite_master\" WHERE \"name\" = 'IX_DownloadJobs_Followed'")
            .SingleAsync(cancellationToken);

        //Assert
        // Partial: the poller's few followed jobs, never the history, which only grows. The statuses by name, as
        // the column stores them — exactly DownloadJobFollowing.FollowedStatuses.
        Assert.Equal(
            "CREATE INDEX \"IX_DownloadJobs_Followed\" ON \"DownloadJobs\" (\"Status\") WHERE \"Status\" IN ('Created', 'Submitted', 'Downloading', 'Paused')",
            definition);
    }

    [Fact]
    public async Task A_job_keeps_its_progress_and_its_block_reason_with_whose_sentence_it_is_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        // MAM-47 — a reason whose JSON runs past the column: a client's name of the column's whole length.
        var reason = Message.Of("downloads.follow.missing", ("client", new string('r', DownloadJobEntity.ErrorMaximumLength)));

        //Act
        var job = await Jobs(db).Create(
            release.Id,
            null,
            "qBittorrent",
            status: DownloadJobStatus.Paused,
            progress: 0.4375,
            blockReason: reason,
            blockReasonOrigin: DownloadJobErrorOrigin.DownloadClient);

        //Assert
        // The reason fitted into its column (MAM-47: its key and values, the longest value cut — the poller compares a new
        // reason with it so), and its origin by name: a value added later needs no migration.
        await using var check = CreateContext();
        var stored = await check.DownloadJobs.AsNoTracking().SingleAsync(x => x.Id == job.Id, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Paused, (double?)0.4375, MessageFit.Fit(reason, MessageJson.ColumnLength), (DownloadJobErrorOrigin?)DownloadJobErrorOrigin.DownloadClient),
            (stored.Status, stored.Progress, stored.BlockReason, stored.BlockReasonOrigin));
        Assert.Equal("downloads.follow.missing", stored.BlockReason!.Key);
        var raw = await check.Database
            .SqlQuery<string>($"SELECT \"BlockReasonOrigin\" AS \"Value\" FROM \"DownloadJobs\"")
            .SingleAsync(cancellationToken);
        Assert.Equal("DownloadClient", raw);
        var rawReason = await check.Database
            .SqlQuery<string>($"SELECT \"BlockReason\" AS \"Value\" FROM \"DownloadJobs\"")
            .SingleAsync(cancellationToken);
        Assert.InRange(rawReason.Length, 1, DownloadJobEntity.ErrorMaximumLength);
    }

    [Fact]
    public async Task A_job_keeps_the_kind_of_its_destination_by_name_and_an_older_job_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        var older = await Jobs(db).Create(release.Id, null, "qBittorrent");
        var folder = await Jobs(db).Create(release.Id, null, "Transmission");
        var tracked = await db.DownloadJobs.SingleAsync(x => x.Id == folder.Id, cancellationToken);

        //Act
        tracked.DestinationKind = DownloadDestinationKind.Folder;
        await db.SaveChangesAsync(cancellationToken);

        //Assert
        // MAM-30 — by name, as every enum on the row: a value added later needs no migration. A row written before the
        // column existed reads null, which the app reads as a category.
        await using var check = CreateContext();
        var stored = await check.DownloadJobs.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.DestinationKind, cancellationToken);
        Assert.Equal((DownloadDestinationKind?)DownloadDestinationKind.Folder, stored[folder.Id]);
        Assert.Null(stored[older.Id]);
        var raw = await check.Database
            .SqlQuery<string>($"SELECT \"DestinationKind\" AS \"Value\" FROM \"DownloadJobs\" WHERE \"DestinationKind\" IS NOT NULL")
            .SingleAsync(cancellationToken);
        Assert.Equal("Folder", raw);
    }

    [Fact]
    public async Task A_jobs_files_keep_their_paths_exactly_and_sizes_past_four_gigabytes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        var job = await Jobs(db).Create(release.Id, null, "qBittorrent");
        // As qBittorrent names them: the root folder first, double spaces kept; and the longest a path column holds.
        const string pack = "Taken. Trilogy  (2008-2014) BDRip 1080p/Taken (2008) BDRip 1080p.mkv";
        var longest = new string('p', PhysicalFileEntity.ServicePathMaximumLength);

        //Act
        await Jobs(db).AddFiles(
            job.Id,
            [
                new DownloadJobFileEntity { RelativePath = pack, SizeBytes = 5_368_709_120, Progress = 0.5, IsWanted = true },
                new DownloadJobFileEntity { RelativePath = longest, IsWanted = false },
                new DownloadJobFileEntity { RelativePath = "extras.nfo" },
            ]);

        //Assert
        await using var check = CreateContext();
        var files = await check.DownloadJobFiles.AsNoTracking().OrderBy(x => x.Position).ToListAsync(cancellationToken);
        Assert.Equal(
            [
                (job.Id, 0, pack, (long?)5_368_709_120, (double?)0.5, (bool?)true),
                (job.Id, 1, longest, null, null, false),
                (job.Id, 2, "extras.nfo", null, null, null),
            ],
            files.Select(x => (x.DownloadJobId, x.Position, x.RelativePath, x.SizeBytes, x.Progress, x.IsWanted)));
    }

    [Fact]
    public async Task A_second_file_in_one_place_of_a_job_is_refused_and_another_job_may_use_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        var job = await Jobs(db).Create(release.Id, null, "qBittorrent");
        var other = await Jobs(db).Create(release.Id, null, "qBittorrent");
        await Jobs(db).AddFiles(job.Id, [new DownloadJobFileEntity { RelativePath = "a.mkv" }]);
        await Jobs(db).AddFiles(other.Id, [new DownloadJobFileEntity { RelativePath = "a.mkv" }]);

        //Act
        var exception = await Record.ExceptionAsync(() => Jobs(db).AddFiles(job.Id, [new DownloadJobFileEntity { RelativePath = "b.mkv" }]));

        //Assert
        // The poller replaces a job's files by position: two rows in one place would be one file listed twice.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: DownloadJobFiles.DownloadJobId, DownloadJobFiles.Position", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(2, await check.DownloadJobFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_job_deletes_its_files_and_no_other_jobs()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var release = await Releases(db).Create(sourceConnectionId: null);
        var job = await Jobs(db).Create(release.Id, null, "qBittorrent");
        var kept = await Jobs(db).Create(release.Id, null, "qBittorrent");
        await Jobs(db).AddFiles(job.Id, [new DownloadJobFileEntity { RelativePath = "a.mkv" }, new DownloadJobFileEntity { RelativePath = "b.mkv" }]);
        await Jobs(db).AddFiles(kept.Id, [new DownloadJobFileEntity { RelativePath = "c.mkv" }]);

        //Act
        await db.DownloadJobs.Where(x => x.Id == job.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: a file row is part of its job, and says nothing once the job is gone.
        await using var check = CreateContext();
        var files = await check.DownloadJobFiles.AsNoTracking().ToListAsync(cancellationToken);
        Assert.Equal((kept.Id, "c.mkv"), Assert.Single(files.Select(x => (x.DownloadJobId, x.RelativePath))));
    }

    [Fact]
    public async Task A_jobs_files_have_their_index()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var indexes = await db.Database
            .SqlQuery<string>($"SELECT \"name\" AS \"Value\" FROM pragma_index_list('DownloadJobFiles') WHERE \"origin\" = 'c'")
            .ToListAsync(cancellationToken);

        //Assert
        // One: unique per place, and led by the job, so it serves the foreign key and "this job's files in order".
        Assert.Equal(["IX_DownloadJobFiles_DownloadJobId_Position"], indexes);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db, string userName) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create(userName, "P@ssword1");

    private static IReleaseDataFactory Releases(ApplicationDbContext db) => new ReleaseDataFactory(db, Encryption, TimeProvider.System);

    private static IDownloadJobDataFactory Jobs(ApplicationDbContext db) => new DownloadJobDataFactory(db, TimeProvider.System);
}
