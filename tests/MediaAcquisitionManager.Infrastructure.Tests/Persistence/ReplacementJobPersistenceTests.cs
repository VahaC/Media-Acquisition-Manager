using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-24 — the scheduled replacement's promises, pinned on the migrated schema (ADR 0030–0034): one replacement per
// download, and a download a replacement points at kept; its files, works and their ids part of it, gone with it; every
// state, kind and match evidence stored by name, the audit actions and target too; an old copy's identity kept to the bit
// and the tick; the following pass's index over the replacements still following only; and the payload kind the sender
// learns kept on the download job by name.
public sealed class ReplacementJobPersistenceTests : DatabaseTestBase
{
    private static readonly IEncryptionService Encryption = new AesGcmEncryptionService(
        Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    public ReplacementJobPersistenceTests() => TestDatabase.CopyMigratedTemplateTo(DatabasePath);

    [Fact]
    public async Task A_second_replacement_of_one_download_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await CreateDownloadJobAsync(db, owner);
        await Replacements(db).Create(job.Id, owner.Id);

        //Act
        var exception = await Record.ExceptionAsync(() => Replacements(db).Create(job.Id, owner.Id));

        //Assert
        // The backstop behind the send's request id: one download brings one replacement.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("UNIQUE constraint failed: ReplacementJobs.DownloadJobId", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.ReplacementJobs.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task A_download_a_replacement_points_at_can_not_be_deleted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await CreateDownloadJobAsync(db, owner);
        await Replacements(db).Create(job.Id, owner.Id);

        //Act
        var exception = await Record.ExceptionAsync(() => db.DownloadJobs.Where(row => row.Id == job.Id).ExecuteDeleteAsync(cancellationToken));

        //Assert
        // Restrict: the download is the replacement's evidence of what was sent.
        var sqlite = Assert.IsType<SqliteException>(exception);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains("FOREIGN KEY constraint failed", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.DownloadJobs.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task Deleting_a_replacement_deletes_its_files_works_and_their_ids_and_no_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var gone = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id);
        var kept = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id);
        foreach (var job in new[] { gone, kept })
        {
            await Replacements(db).AddFile(job.Id, $"/media/movies/{job.Id}.mkv", TestMedia.File(inode: 1));
            await Replacements(db).AddWork(
                job.Id, Guid.NewGuid(), MediaWorkKind.Movie, "Alien", 1979, MatchEvidence.Identifier, false, "This release is Alien (1979)",
                [(MediaIdentifierScheme.Imdb, "tt0078748", false)]);
        }

        //Act
        await db.ReplacementJobs.Where(row => row.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the old copies, the works and their ids are part of their job, and say nothing once it is gone.
        await using var check = CreateContext();
        Assert.Equal(kept.Id, (await check.ReplacementJobFiles.SingleAsync(cancellationToken)).ReplacementJobId);
        var work = await check.ReplacementJobWorks.SingleAsync(cancellationToken);
        Assert.Equal(kept.Id, work.ReplacementJobId);
        Assert.Equal(work.Id, (await check.ReplacementJobWorkIdentifiers.SingleAsync(cancellationToken)).ReplacementJobWorkId);
    }

    [Fact]
    public async Task A_replacement_keeps_its_old_copys_identity_to_the_bit_and_the_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id);

        //Act
        // A device past long.MaxValue as an unsigned st_dev, a size past 4 GiB and a time with sub-millisecond ticks.
        await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien.mkv", TestMedia.File(inode: 42, sizeBytes: 5_000_000_000, linkCount: 2));

        //Assert
        await using var check = CreateContext();
        var file = await check.ReplacementJobFiles.SingleAsync(cancellationToken);
        Assert.Equal(
            (TestMedia.DeviceId, 42L, 5_000_000_000L, TestMedia.ModifiedUtc.Ticks, (int?)2),
            (file.DeviceId, file.Inode, file.SizeBytes, file.ModifiedUtc.Ticks, file.LinkCount));
    }

    [Fact]
    public async Task The_states_kinds_evidence_and_schemes_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.NeedsReview, "Why.");
        await Replacements(db).AddWork(
            job.Id, Guid.NewGuid(), MediaWorkKind.Series, "Doctor Who", 2005, MatchEvidence.TitleAndYear, true,
            "This release is Doctor Who (2005), season 2", [(MediaIdentifierScheme.TmdbTv, "57243", true)]);

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT "State" || '/' || "Kind" || '/' || "Evidence" || '/' || "Scheme" AS "Value"
                FROM "ReplacementJobs"
                JOIN "ReplacementJobWorks" ON "ReplacementJobWorks"."ReplacementJobId" = "ReplacementJobs"."Id"
                JOIN "ReplacementJobWorkIdentifiers" ON "ReplacementJobWorkIdentifiers"."ReplacementJobWorkId" = "ReplacementJobWorks"."Id"
                """)
            .SingleAsync(cancellationToken);

        //Assert
        // By name, like every other enum: a state added later never changes what a stored row means.
        Assert.Equal("NeedsReview/Series/TitleAndYear/TmdbTv", stored);
    }

    [Fact]
    public async Task The_audit_actions_and_target_of_a_replacement_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id);
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        auditTrail.Record(AuditAction.ReplacementScheduled, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementWorkConfirmed, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "This release is Alien (1979)"));
        auditTrail.Record(AuditAction.ReplacementCancelled, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var rows = await db.Database
            .SqlQueryRaw<string>("SELECT \"Action\" || '/' || \"TargetKind\" || '/' || \"TargetName\" AS \"Value\" FROM \"AuditEvents\" ORDER BY \"Action\"")
            .ToListAsync(cancellationToken);

        //Assert
        Assert.Equal(
            [
                "ReplacementCancelled/ReplacementJob/Alien (1979) BDRip",
                "ReplacementScheduled/ReplacementJob/Alien (1979) BDRip",
                "ReplacementWorkConfirmed/ReplacementJob/This release is Alien (1979)",
            ],
            rows);
    }

    [Fact]
    public async Task The_followed_index_holds_only_the_replacements_still_following_their_download()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT \"sql\" AS \"Value\" FROM \"sqlite_master\" WHERE \"name\" = 'IX_ReplacementJobs_Followed'")
            .SingleAsync(cancellationToken);

        //Assert
        // Partial: the few still going, never the history. The states by name — exactly ReplacementJobStates.Followed.
        Assert.Equal(
            "CREATE INDEX \"IX_ReplacementJobs_Followed\" ON \"ReplacementJobs\" (\"State\") WHERE \"State\" IN ('Scheduled', 'Submitted', 'Downloading', 'Paused', 'NeedsReview')",
            definition);
    }

    [Fact]
    public async Task A_download_job_keeps_the_payload_kind_its_sender_learned_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await CreateDownloadJobAsync(db, owner);

        //Act
        await db.DownloadJobs.Where(row => row.Id == job.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PayloadKind, ReleasePayloadKind.TorrentFile), cancellationToken);

        //Assert
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT \"PayloadKind\" AS \"Value\" FROM \"DownloadJobs\"")
            .SingleAsync(cancellationToken);
        Assert.Equal("TorrentFile", stored);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static Task<UserEntity> CreateUserAsync(ApplicationDbContext db) =>
        new UserDataFactory(db, new Pbkdf2PasswordHasher(1_000), TimeProvider.System).Create("owner", "P@ssword1");

    private static async Task<DownloadJobEntity> CreateDownloadJobAsync(ApplicationDbContext db, UserEntity owner)
    {
        var release = await new ReleaseDataFactory(db, Encryption, TimeProvider.System).Create(sourceConnectionId: null);
        return await new DownloadJobDataFactory(db, TimeProvider.System).Create(release.Id, null, "qBittorrent", requestedByUserId: owner.Id);
    }

    private static IReplacementJobDataFactory Replacements(ApplicationDbContext db) => new ReplacementJobDataFactory(db, TimeProvider.System);
}
