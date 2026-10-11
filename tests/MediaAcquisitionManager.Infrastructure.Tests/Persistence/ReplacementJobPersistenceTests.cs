using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Library;
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
// learns kept on the download job by name. MAM-25 (ADR 0035) — the verification evidence: a job's new files and their ids
// part of it, gone with it, one per place in the client's list; their kind, the new states and the check's audit actions
// stored by name; the evidence kept to the bit and the tick; no key into the library's integrations or works, nor from an
// old copy to its new file; and the verifier's index over the jobs being checked only. MAM-26 (ADR 0030) — the quarantine:
// a job's names part of it, gone with it, one per place in its plan; the new job and name states, the name kinds, the
// quarantine's audit actions and the name target stored by name, a name named by the file's own name; a name's identity
// and the job's new times kept to the bit and the tick; and the mover's index over the jobs it takes up only. MAM-27 — a
// copy's drop kept to the tick beside its drop-only mark, a copy neither dropped nor so marked unless told; the owner's
// three new audit actions and the copy target stored by name, a copy named by the file's own name; and a check forgotten
// takes its new files and their ids with it and clears the undropped copies only, a dropped one keeping its history.
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

    [Fact]
    public async Task Deleting_a_replacement_deletes_its_new_files_and_their_ids_and_no_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var gone = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        var kept = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        foreach (var job in new[] { gone, kept })
            await Replacements(db).AddNewFile(job.Id, 0, "Alien.1979.2160p.mkv", LibraryFileKind.Movie, [(MediaIdentifierScheme.Tmdb, "348", false)]);

        //Act
        await db.ReplacementJobs.Where(row => row.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the evidence is part of its job, and says nothing once it is gone.
        await using var check = CreateContext();
        var file = await check.ReplacementJobNewFiles.SingleAsync(cancellationToken);
        Assert.Equal(kept.Id, file.ReplacementJobId);
        Assert.Equal(file.Id, (await check.ReplacementJobNewFileIdentifiers.SingleAsync(cancellationToken)).ReplacementJobNewFileId);
    }

    [Fact]
    public async Task A_second_new_file_at_one_place_of_a_jobs_download_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        await Replacements(db).AddNewFile(job.Id, 3, "Pack/Alien.1979.mkv");

        //Act
        var exception = await Record.ExceptionAsync(() => Replacements(db).AddNewFile(job.Id, 3, "Pack/Aliens.1986.mkv"));

        //Assert
        // One row per place in the client's list: the rows are replaced whole, never added to.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: ReplacementJobNewFiles.ReplacementJobId, ReplacementJobNewFiles.Position", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.ReplacementJobNewFiles.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task The_verification_states_kinds_and_schemes_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var checking = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        var verified = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Verified);
        await Replacements(db).AddNewFile(checking.Id, 0, "Extras/Making of.mkv", LibraryFileKind.Other);
        await Replacements(db).AddNewFile(
            verified.Id, 0, "Penguins.S02E05.mkv", LibraryFileKind.Episode, [(MediaIdentifierScheme.TmdbTv, "1044", true), (MediaIdentifierScheme.Imdb, "tt1", false)]);

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT "State" || '/' || "Kind" || '/' || COALESCE("Scheme", '-') || '/' || COALESCE("OfSeries", '-') AS "Value"
                FROM "ReplacementJobs"
                JOIN "ReplacementJobNewFiles" ON "ReplacementJobNewFiles"."ReplacementJobId" = "ReplacementJobs"."Id"
                LEFT JOIN "ReplacementJobNewFileIdentifiers" ON "ReplacementJobNewFileIdentifiers"."ReplacementJobNewFileId" = "ReplacementJobNewFiles"."Id"
                ORDER BY 1
                """)
            .ToListAsync(cancellationToken);

        //Assert
        // By name, like every other enum: a member added later never changes what a stored row means.
        Assert.Equal(["Inspecting/Other/-/-", "Verified/Episode/Imdb/0", "Verified/Episode/TmdbTv/1"], stored);
    }

    [Fact]
    public async Task The_verification_audit_actions_are_stored_by_name_with_no_actor()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        // Nobody clicked: the app's own check moved the job.
        auditTrail.Record(AuditAction.ReplacementCheckStarted, null, AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementVerified, null, AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementNeedsReview, null, AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementFailed, null, AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var rows = await db.Database
            .SqlQueryRaw<string>(
                "SELECT \"Action\" || '/' || COALESCE(\"ActorUserName\", '-') || '/' || \"TargetKind\" AS \"Value\" FROM \"AuditEvents\" ORDER BY \"Action\"")
            .ToListAsync(cancellationToken);

        //Assert
        Assert.Equal(
            [
                "ReplacementCheckStarted/-/ReplacementJob",
                "ReplacementFailed/-/ReplacementJob",
                "ReplacementNeedsReview/-/ReplacementJob",
                "ReplacementVerified/-/ReplacementJob",
            ],
            rows);
    }

    [Fact]
    public async Task The_verifying_index_holds_only_the_replacements_whose_new_files_are_being_checked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT \"sql\" AS \"Value\" FROM \"sqlite_master\" WHERE \"name\" = 'IX_ReplacementJobs_Verifying'")
            .SingleAsync(cancellationToken);

        //Assert
        // Partial: the few being checked, never the history. The states by name — exactly ReplacementJobStates.Verifying.
        Assert.Equal(
            "CREATE INDEX \"IX_ReplacementJobs_Verifying\" ON \"ReplacementJobs\" (\"State\") WHERE \"State\" IN ('Downloaded', 'Inspecting')",
            definition);
    }

    [Fact]
    public async Task The_verification_evidence_is_kept_to_the_bit_and_the_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        var oldCopy = await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien.mkv", TestMedia.File(inode: 1));
        var newFile = await Replacements(db).AddNewFile(job.Id, 0, "Alien.1979.2160p.mkv", LibraryFileKind.Movie);
        var startedUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var askedUtc = startedUtc.AddMinutes(2).AddTicks(1);
        var problem = new string('x', ReplacementJobFileEntity.ProblemMaximumLength);

        //Act
        await db.ReplacementJobs.Where(row => row.Id == job.Id).ExecuteUpdateAsync(
            setters => setters.SetProperty(row => row.InspectionStartedUtc, startedUtc).SetProperty(row => row.LibraryAskedUtc, askedUtc), cancellationToken);
        await db.ReplacementJobFiles.Where(row => row.Id == oldCopy.Id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.PairedNewFileId, newFile.Id)
                .SetProperty(row => row.EpisodeNumberEnd, 6)
                .SetProperty(row => row.DurationMilliseconds, 6_990_000L)
                .SetProperty(row => row.Problem, problem),
            cancellationToken);
        // A device past long.MaxValue as an unsigned st_dev, a size past 4 GiB and a time with sub-millisecond ticks.
        await db.ReplacementJobNewFiles.Where(row => row.Id == newFile.Id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.DeviceId, TestMedia.DeviceId)
                .SetProperty(row => row.Inode, 42L)
                .SetProperty(row => row.SizeBytes, 5_000_000_000L)
                .SetProperty(row => row.ModifiedUtc, TestMedia.ModifiedUtc)
                .SetProperty(row => row.ClientProgress, 0.999_999_9)
                .SetProperty(row => row.Refreshed, true)
                .SetProperty(row => row.EpisodeNumberEnd, 6)
                .SetProperty(row => row.ReadUtc, askedUtc),
            cancellationToken);

        //Assert
        await using var check = CreateContext();
        var storedJob = await check.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal((startedUtc.Ticks, askedUtc.Ticks), (storedJob.InspectionStartedUtc!.Value.Ticks, storedJob.LibraryAskedUtc!.Value.Ticks));
        var storedCopy = await check.ReplacementJobFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            ((Guid?)newFile.Id, (int?)6, (long?)6_990_000, problem),
            (storedCopy.PairedNewFileId, storedCopy.EpisodeNumberEnd, storedCopy.DurationMilliseconds, storedCopy.Problem));
        var storedFile = await check.ReplacementJobNewFiles.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            ((long?)TestMedia.DeviceId, (long?)42, (long?)5_000_000_000, TestMedia.ModifiedUtc.Ticks, (double?)0.999_999_9, true, (int?)6, askedUtc.Ticks),
            (storedFile.DeviceId, storedFile.Inode, storedFile.SizeBytes, storedFile.ModifiedUtc!.Value.Ticks, storedFile.ClientProgress, storedFile.Refreshed,
                storedFile.EpisodeNumberEnd, storedFile.ReadUtc!.Value.Ticks));
    }

    [Fact]
    public async Task The_verification_evidence_keeps_no_key_into_the_library_or_from_an_old_copy_to_its_new_file()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Inspecting);
        var oldCopy = await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien.mkv", TestMedia.File(inode: 1));
        var newFile = await Replacements(db).AddNewFile(job.Id, 0, "Alien.1979.2160p.mkv", LibraryFileKind.Movie);

        //Act
        // Snapshot ids of an integration and a work no row holds, and a pair whose new file the next check replaced.
        var exception = await Record.ExceptionAsync(async () =>
        {
            await db.ReplacementJobNewFiles.Where(row => row.Id == newFile.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.LibraryIntegrationId, Guid.NewGuid()).SetProperty(row => row.MediaWorkId, Guid.NewGuid()),
                cancellationToken);
            await db.ReplacementJobFiles.Where(row => row.Id == oldCopy.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.PairedNewFileId, Guid.NewGuid()), cancellationToken);
        });

        //Assert
        // A key from ReplacementJobFiles would rebuild that table; keys into the library would lose the evidence to a sync.
        Assert.Null(exception);
    }

    // ── the quarantine (MAM-26) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_replacement_deletes_its_names_and_no_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var gone = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Quarantined);
        var kept = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Quarantined);
        foreach (var job in new[] { gone, kept })
        {
            var copy = await Replacements(db).AddFile(job.Id, $"/media/movies/{job.Id}.mkv", TestMedia.File(inode: 1));
            await Replacements(db).AddName(
                job.Id, copy.Id, 0, ReplacementNameKind.Video, copy.AppPath, $"/media/.mam-quarantine/{job.Id}/movies/{job.Id}.mkv", "/media",
                TestMedia.File(inode: 1), ReplacementNameState.Quarantined);
        }

        //Act
        await db.ReplacementJobs.Where(row => row.Id == gone.Id).ExecuteDeleteAsync(cancellationToken);

        //Assert
        // Cascade: the names are part of their job, and say nothing once it is gone.
        await using var check = CreateContext();
        Assert.Equal(kept.Id, (await check.ReplacementJobNames.SingleAsync(cancellationToken)).ReplacementJobId);
    }

    [Fact]
    public async Task A_second_name_at_one_place_of_a_jobs_plan_is_refused()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Quarantining);
        var copy = await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien.mkv", TestMedia.File(inode: 1));
        await Replacements(db).AddName(
            job.Id, copy.Id, 1, ReplacementNameKind.Companion, "/media/movies/Alien (1979)/Alien.srt",
            $"/media/.mam-quarantine/{job.Id}/movies/Alien (1979)/Alien.srt", "/media", TestMedia.File(inode: 2, sizeBytes: 80_000));

        //Act
        var exception = await Record.ExceptionAsync(() => Replacements(db).AddName(
            job.Id, copy.Id, 1, ReplacementNameKind.Companion, "/media/movies/Alien (1979)/Alien.nfo",
            $"/media/.mam-quarantine/{job.Id}/movies/Alien (1979)/Alien.nfo", "/media", TestMedia.File(inode: 3, sizeBytes: 4_000)));

        //Assert
        // One name per place in the plan, which is what lets a restart say how far the move got.
        var sqlite = Assert.IsType<SqliteException>(Assert.IsType<DbUpdateException>(exception).InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Contains(
            "UNIQUE constraint failed: ReplacementJobNames.ReplacementJobId, ReplacementJobNames.Position", sqlite.Message, StringComparison.Ordinal);
        await using var check = CreateContext();
        Assert.Equal(1, await check.ReplacementJobNames.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task The_quarantine_states_and_name_kinds_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        // Every state MAM-26 adds, on a job and on a name, each once.
        (ReplacementJobState Job, ReplacementNameKind Kind, ReplacementNameState Name)[] planted =
        [
            (ReplacementJobState.Quarantining, ReplacementNameKind.Video, ReplacementNameState.Planned),
            (ReplacementJobState.Quarantining, ReplacementNameKind.Companion, ReplacementNameState.Moving),
            (ReplacementJobState.Quarantined, ReplacementNameKind.Video, ReplacementNameState.Quarantined),
            (ReplacementJobState.Quarantined, ReplacementNameKind.Companion, ReplacementNameState.Stayed),
            (ReplacementJobState.Restoring, ReplacementNameKind.Video, ReplacementNameState.Restoring),
            (ReplacementJobState.Restored, ReplacementNameKind.Video, ReplacementNameState.Restored),
            (ReplacementJobState.Deleting, ReplacementNameKind.Video, ReplacementNameState.Deleting),
            (ReplacementJobState.Completed, ReplacementNameKind.Video, ReplacementNameState.Deleted),
            (ReplacementJobState.Completed, ReplacementNameKind.Companion, ReplacementNameState.Lost),
        ];
        foreach (var state in planted.GroupBy(row => row.Job))
        {
            var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, state.Key);
            var copy = await Replacements(db).AddFile(job.Id, $"/media/movies/{job.Id}.mkv", TestMedia.File(inode: 1));
            foreach (var (row, position) in state.Select((row, position) => (row, position)))
            {
                await Replacements(db).AddName(
                    job.Id, copy.Id, position, row.Kind, $"/media/movies/{job.Id}.{position}", $"/media/.mam-quarantine/{job.Id}/movies/{job.Id}.{position}",
                    "/media", TestMedia.File(inode: 100 + position), row.Name);
            }
        }

        //Act
        var stored = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT "ReplacementJobs"."State" || '/' || "Kind" || '/' || "ReplacementJobNames"."State" AS "Value"
                FROM "ReplacementJobs"
                JOIN "ReplacementJobNames" ON "ReplacementJobNames"."ReplacementJobId" = "ReplacementJobs"."Id"
                ORDER BY 1
                """)
            .ToListAsync(cancellationToken);

        //Assert
        // By name, like every other enum: a member added later never changes what a stored row means.
        Assert.Equal(
            [
                "Completed/Companion/Lost", "Completed/Video/Deleted", "Deleting/Video/Deleting", "Quarantined/Companion/Stayed",
                "Quarantined/Video/Quarantined", "Quarantining/Companion/Moving", "Quarantining/Video/Planned", "Restored/Video/Restored",
                "Restoring/Video/Restoring",
            ],
            stored);
    }

    [Fact]
    public async Task A_quarantine_name_keeps_its_identity_to_the_bit_and_the_tick()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Quarantined);
        var copy = await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien.mkv", TestMedia.File(inode: 42));
        var movedUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var checkedUtc = movedUtc.AddMinutes(-1).AddTicks(3);
        var problem = new string('x', ReplacementJobNameEntity.ProblemMaximumLength);

        //Act
        // A device past long.MaxValue as an unsigned st_dev, a size past 4 GiB, a time with sub-millisecond ticks and a
        // second name.
        await Replacements(db).AddName(
            job.Id, copy.Id, 0, ReplacementNameKind.Video, copy.AppPath, $"/media/.mam-quarantine/{job.Id}/movies/Alien (1979)/Alien.mkv", "/media",
            TestMedia.File(inode: 42, sizeBytes: 5_000_000_000, linkCount: 2), ReplacementNameState.Quarantined, problem, movedUtc);
        await db.ReplacementJobs.Where(row => row.Id == job.Id).ExecuteUpdateAsync(
            setters => setters
                .SetProperty(row => row.QuarantineCheckedUtc, checkedUtc)
                .SetProperty(row => row.QuarantinedUtc, movedUtc)
                .SetProperty(row => row.LibraryNoticeDueUtc, movedUtc),
            cancellationToken);

        //Assert
        await using var check = CreateContext();
        var name = await check.ReplacementJobNames.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (copy.Id, TestMedia.DeviceId, 42L, 5_000_000_000L, TestMedia.ModifiedUtc.Ticks, (int?)2, problem, movedUtc.Ticks),
            (name.ReplacementJobFileId, name.DeviceId, name.Inode, name.SizeBytes, name.ModifiedUtc.Ticks, name.LinkCount, name.Problem,
                name.MovedUtc!.Value.Ticks));
        var stored = await check.ReplacementJobs.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            (checkedUtc.Ticks, movedUtc.Ticks, movedUtc.Ticks),
            (stored.QuarantineCheckedUtc!.Value.Ticks, stored.QuarantinedUtc!.Value.Ticks, stored.LibraryNoticeDueUtc!.Value.Ticks));
    }

    [Fact]
    public async Task The_quarantine_audit_actions_and_the_name_target_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.Quarantining);
        var copy = await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien (1979).mkv", TestMedia.File(inode: 1));
        var name = await Replacements(db).AddName(
            job.Id, copy.Id, 0, ReplacementNameKind.Video, copy.AppPath, $"/media/.mam-quarantine/{job.Id}/movies/Alien (1979)/Alien (1979).mkv",
            "/media", TestMedia.File(inode: 1));
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        // The mover's rows have no actor; the owner's actions carry him.
        auditTrail.Record(AuditAction.ReplacementQuarantineStarted, null, AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementNameMoving, null, AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementNameMoved, null, AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementNameNotMoved, null, AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementQuarantined, null, AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementRetried, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementRestoreStarted, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementNameRestoring, AuditActor.From(owner), AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementNameRestored, AuditActor.From(owner), AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementNameNotRestored, AuditActor.From(owner), AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementRestored, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementDeleteStarted, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementNameDeleting, AuditActor.From(owner), AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementNameDeleted, AuditActor.From(owner), AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementNameNotDeleted, AuditActor.From(owner), AuditTarget.ReplacementJobName(name));
        auditTrail.Record(AuditAction.ReplacementCompleted, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT "Action" || '/' || COALESCE("ActorUserName", '-') || '/' || "TargetKind" || '/' || "TargetName" AS "Value"
                FROM "AuditEvents" ORDER BY "Action"
                """)
            .ToListAsync(cancellationToken);
        var nameTargets = await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.TargetKind == AuditTargetKind.ReplacementJobName)
            .Select(audit => audit.TargetId)
            .Distinct()
            .ToListAsync(cancellationToken);

        //Assert
        // A name is named by the file's own name, and found again by its row's id.
        Assert.Equal(
            [
                "ReplacementCompleted/owner/ReplacementJob/Alien (1979) BDRip",
                "ReplacementDeleteStarted/owner/ReplacementJob/Alien (1979) BDRip",
                "ReplacementNameDeleted/owner/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameDeleting/owner/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameMoved/-/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameMoving/-/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameNotDeleted/owner/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameNotMoved/-/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameNotRestored/owner/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameRestored/owner/ReplacementJobName/Alien (1979).mkv",
                "ReplacementNameRestoring/owner/ReplacementJobName/Alien (1979).mkv",
                "ReplacementQuarantineStarted/-/ReplacementJob/Alien (1979) BDRip",
                "ReplacementQuarantined/-/ReplacementJob/Alien (1979) BDRip",
                "ReplacementRestoreStarted/owner/ReplacementJob/Alien (1979) BDRip",
                "ReplacementRestored/owner/ReplacementJob/Alien (1979) BDRip",
                "ReplacementRetried/owner/ReplacementJob/Alien (1979) BDRip",
            ],
            rows);
        Assert.Equal([(Guid?)name.Id], nameTargets);
    }

    [Fact]
    public async Task The_quarantine_index_holds_only_the_replacements_the_mover_takes_up()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        //Act
        var definition = await db.Database
            .SqlQueryRaw<string>("SELECT \"sql\" AS \"Value\" FROM \"sqlite_master\" WHERE \"name\" = 'IX_ReplacementJobs_Quarantine'")
            .SingleAsync(cancellationToken);

        //Assert
        // Partial: the few the mover takes up, never the history. The states by name — exactly ReplacementJobStates.Quarantine.
        Assert.Equal(
            "CREATE INDEX \"IX_ReplacementJobs_Quarantine\" ON \"ReplacementJobs\" (\"State\") WHERE \"State\" IN ('Verified', 'Quarantining')",
            definition);
    }

    [Fact]
    public async Task A_ticked_copy_keeps_its_drop_to_the_tick_beside_its_drop_only_mark()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.NeedsReview);
        var droppedUtc = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);

        //Act
        var dropped = await Replacements(db).AddFile(
            job.Id, "/media/tv-shows/Yellowstone/Season 02/Yellowstone S02E05.mkv", TestMedia.File(inode: 42), position: 0,
            problem: "Jellyfin “Living room” now numbers the old copy “Yellowstone S02E05.mkv” season 2, episode 6.", onlyDropSettles: true,
            droppedUtc: droppedUtc);
        var kept = await Replacements(db).AddFile(job.Id, "/media/tv-shows/Yellowstone/Season 02/Yellowstone S02E06.mkv", TestMedia.File(inode: 43), position: 1);

        //Assert
        await using var check = CreateContext();
        var copies = await check.ReplacementJobFiles.AsNoTracking().OrderBy(copy => copy.Position).ToListAsync(cancellationToken);
        // A copy written before MAM-27, or never dropped, reads as in the job and as one a Retry may settle.
        Assert.Equal(
            [(dropped.Id, (long?)droppedUtc.Ticks, true), (kept.Id, (long?)null, false)],
            copies.Select(copy => (copy.Id, copy.DroppedUtc?.Ticks, copy.OnlyDropSettles)));
    }

    [Fact]
    public async Task The_owners_review_audit_actions_and_the_copy_target_are_stored_by_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.NeedsReview);
        var copy = await Replacements(db).AddFile(job.Id, "/media/movies/Alien (1979)/Alien (1979).mkv", TestMedia.File(inode: 1));
        var auditTrail = new AuditTrail(db, TimeProvider.System);
        auditTrail.Record(AuditAction.ReplacementCheckRetried, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementCancelledAlone, AuditActor.From(owner), AuditTarget.ReplacementJob(job, "Alien (1979) BDRip"));
        auditTrail.Record(AuditAction.ReplacementCopyDropped, AuditActor.From(owner), AuditTarget.ReplacementJobFile(copy));
        await db.SaveChangesAsync(cancellationToken);

        //Act
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT "Action" || '/' || COALESCE("ActorUserName", '-') || '/' || "TargetKind" || '/' || "TargetName" AS "Value"
                FROM "AuditEvents" ORDER BY "Action"
                """)
            .ToListAsync(cancellationToken);
        var copyTargets = await db.AuditEvents.AsNoTracking()
            .Where(audit => audit.TargetKind == AuditTargetKind.ReplacementJobFile)
            .Select(audit => audit.TargetId)
            .ToListAsync(cancellationToken);

        //Assert
        // A copy is named by the file's own name, and found again by its row's id.
        Assert.Equal(
            [
                "ReplacementCancelledAlone/owner/ReplacementJob/Alien (1979) BDRip",
                "ReplacementCheckRetried/owner/ReplacementJob/Alien (1979) BDRip",
                "ReplacementCopyDropped/owner/ReplacementJobFile/Alien (1979).mkv",
            ],
            rows);
        Assert.Equal([(Guid?)copy.Id], copyTargets);
    }

    [Fact]
    public async Task Forgetting_a_check_deletes_its_new_files_and_their_ids_and_clears_the_undropped_copies_only()
    {
        //Arrange
        const string Renumbered = "Jellyfin “Living room” now numbers the old copy “Yellowstone S02E05.mkv” season 2, episode 6.";
        const string NoNewFile = "No new file is season 2, episode 6, so “Yellowstone S02E06.mkv” has nothing to replace it.";
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        var owner = await CreateUserAsync(db);
        var job = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.NeedsReview);
        var other = await Replacements(db).Create((await CreateDownloadJobAsync(db, owner)).Id, owner.Id, ReplacementJobState.NeedsReview);
        var newFile = await Replacements(db).AddNewFile(
            job.Id, 0, "Yellowstone.S02E05.mkv", LibraryFileKind.Episode, [(MediaIdentifierScheme.Tvdb, "341164", true)]);
        var othersNewFile = await Replacements(db).AddNewFile(
            other.Id, 0, "Alien.1979.2160p.mkv", LibraryFileKind.Movie, [(MediaIdentifierScheme.Tmdb, "348", false)]);
        var droppedUtc = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var dropped = await Replacements(db).AddFile(
            job.Id, "/media/tv-shows/Yellowstone/Season 02/Yellowstone S02E05.mkv", TestMedia.File(inode: 42), position: 0, pairedNewFileId: newFile.Id,
            problem: Renumbered, onlyDropSettles: true, droppedUtc: droppedUtc);
        var stopped = await Replacements(db).AddFile(
            job.Id, "/media/tv-shows/Yellowstone/Season 02/Yellowstone S02E06.mkv", TestMedia.File(inode: 43), position: 1, problem: NoNewFile,
            onlyDropSettles: true);
        var paired = await Replacements(db).AddFile(
            job.Id, "/media/tv-shows/Yellowstone/Season 02/Yellowstone S02E07.mkv", TestMedia.File(inode: 44), position: 2, pairedNewFileId: newFile.Id);
        var othersCopy = await Replacements(db).AddFile(
            other.Id, "/media/movies/Alien (1979)/Alien (1979).mkv", TestMedia.File(inode: 45), pairedNewFileId: othersNewFile.Id, problem: NoNewFile,
            onlyDropSettles: true);

        //Act
        await db.ForgetCheckAsync(job.Id);

        //Assert
        await using var check = CreateContext();
        // The job's new files go with their ids; the other job's evidence stays.
        Assert.Equal([othersNewFile.Id], await check.ReplacementJobNewFiles.Select(file => file.Id).ToListAsync(cancellationToken));
        Assert.Equal(
            [othersNewFile.Id],
            await check.ReplacementJobNewFileIdentifiers.Select(identifier => identifier.ReplacementJobNewFileId).ToListAsync(cancellationToken));
        var copies = (await check.ReplacementJobFiles.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(copy => copy.Inode);
        // A dropped copy keeps its last pair and problem as history; the others start the next pairing from nothing.
        Assert.Equal(
            [
                (dropped.Id, (Guid?)newFile.Id, (string?)Renumbered, true, (DateTime?)droppedUtc),
                (stopped.Id, (Guid?)null, (string?)null, false, (DateTime?)null),
                (paired.Id, (Guid?)null, (string?)null, false, (DateTime?)null),
                (othersCopy.Id, (Guid?)othersNewFile.Id, (string?)NoNewFile, true, (DateTime?)null),
            ],
            copies.Select(copy => (copy.Id, copy.PairedNewFileId, copy.Problem, copy.OnlyDropSettles, copy.DroppedUtc)));
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
