using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaAcquisitionManager.Infrastructure.Persistence;

/// <summary>
/// The app's one EF Core context over the SQLite database. Controllers and services take it
/// directly — there is no repository layer — and the tests run it against a real SQLite file.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<InstallationEntity> Installations => Set<InstallationEntity>();

    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    public DbSet<IntegrationConnectionEntity> IntegrationConnections => Set<IntegrationConnectionEntity>();

    public DbSet<PathMappingEntity> PathMappings => Set<PathMappingEntity>();

    public DbSet<DestinationMappingEntity> DestinationMappings => Set<DestinationMappingEntity>();

    public DbSet<MediaWorkEntity> MediaWorks => Set<MediaWorkEntity>();

    public DbSet<MediaWorkTitleEntity> MediaWorkTitles => Set<MediaWorkTitleEntity>();

    public DbSet<MediaWorkIdentifierEntity> MediaWorkIdentifiers => Set<MediaWorkIdentifierEntity>();

    public DbSet<MediaVersionEntity> MediaVersions => Set<MediaVersionEntity>();

    public DbSet<PhysicalFileEntity> PhysicalFiles => Set<PhysicalFileEntity>();

    public DbSet<LibraryLocationEntity> LibraryLocations => Set<LibraryLocationEntity>();

    public DbSet<LibraryLocationFolderEntity> LibraryLocationFolders => Set<LibraryLocationFolderEntity>();

    public DbSet<LibrarySyncRunEntity> LibrarySyncRuns => Set<LibrarySyncRunEntity>();

    public DbSet<AllowedRootEntity> AllowedRoots => Set<AllowedRootEntity>();

    public DbSet<MediaInspectionEntity> MediaInspections => Set<MediaInspectionEntity>();

    public DbSet<MediaInspectionTrackEntity> MediaInspectionTracks => Set<MediaInspectionTrackEntity>();

    public DbSet<FileInspectionRunEntity> FileInspectionRuns => Set<FileInspectionRunEntity>();

    public DbSet<ReleaseEntity> Releases => Set<ReleaseEntity>();

    public DbSet<DownloadJobEntity> DownloadJobs => Set<DownloadJobEntity>();

    public DbSet<DownloadJobFileEntity> DownloadJobFiles => Set<DownloadJobFileEntity>();

    public DbSet<SearchEntity> Searches => Set<SearchEntity>();

    public DbSet<SearchSourceEntity> SearchSources => Set<SearchSourceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserEntity>(e =>
        {
            // MAM-4 — uniqueness is on the normalised name: SQLite compares text case-sensitively,
            // so an index on UserName would let "Alice" and "alice" be two accounts. Also the
            // index sign-in looks users up by.
            e.HasIndex(x => x.NormalizedUserName).IsUnique();
        });

        modelBuilder.Entity<InstallationEntity>(e =>
        {
            // MAM-4 — Restrict: the owner can't be deleted. The API refuses with a 409 first; this
            // makes the database refuse too, should a code path ever forget to ask.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserSessionEntity>(e =>
        {
            // Every refresh looks its session up by the presented token's hash, and a hash must
            // name exactly one session.
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            // Not unique: it only has to find the session a replayed (already rotated) token
            // belonged to.
            e.HasIndex(x => x.PreviousRefreshTokenHash);
            // Cascade: deleting a user ends their sessions with them.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEventEntity>(e =>
        {
            // Stored by name, so reordering an enum never changes what a stored row means; the
            // lengths leave room for longer member names without a migration.
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(48);
            e.Property(x => x.TargetKind).HasConversion<string>().HasMaxLength(32);
            // The audit trail is read newest first.
            e.HasIndex(x => x.CreatedUtc);
            // SetNull: audit rows outlive the user who acted; ActorUserName keeps the name.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Every enum below is stored by name (the AuditAction reason): a member added or reordered
        // later never changes what a stored row means, and needs no migration.

        modelBuilder.Entity<IntegrationConnectionEntity>(e =>
        {
            // MAM-7 — uniqueness on the normalised name, for the NormalizedUserName reason: SQLite
            // compares text case-sensitively. The service answers 409 first; this settles two
            // creates racing each other.
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            // The settings list filters by kind in SQL.
            e.HasIndex(x => x.Kind);
            // Cascade: the mappings are part of the connection (its one navigation, replaced by its
            // one Save), meaningless without it.
            e.HasMany(x => x.PathMappings).WithOne()
                .HasForeignKey(x => x.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PathMappingEntity>(e =>
        {
            // A backstop for PathMappingRules, which also catch the letter-case variants of a
            // Windows path that this ordinal index can't. Leading with the owner, so it also serves
            // the foreign key.
            e.HasIndex(x => new { x.IntegrationConnectionId, x.ServicePrefix }).IsUnique();
        });

        modelBuilder.Entity<DestinationMappingEntity>(e =>
        {
            // One logical name per client, in any letter case. Leading with the client, so it also
            // serves the foreign key.
            e.HasIndex(x => new { x.DownloadClientConnectionId, x.NormalizedName }).IsUnique();
            // Cascade: a destination of a removed client can't be used any more; the jobs that were
            // sent to it keep its name in DestinationName.
            e.HasOne<IntegrationConnectionEntity>().WithMany()
                .HasForeignKey(x => x.DownloadClientConnectionId)
                .OnDelete(DeleteBehavior.Cascade);
            // MAM-11 — at most one default destination for the whole installation: unique over the
            // default rows only, so any number of others can sit beside it, and none at all is allowed
            // (a removed default is never replaced automatically). Moving the default clears the old one
            // before setting the new, in one save: SQLite checks the index as each row changes, not at
            // commit. The service answers first; this settles two saves racing each other. SQLite
            // stores a bool as 0 or 1.
            e.HasIndex(x => x.IsDefault, "IX_DestinationMappings_OneDefault")
                .IsUnique()
                .HasFilter("\"IsDefault\" = 1");
        });

        modelBuilder.Entity<MediaWorkEntity>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<MediaWorkTitleEntity>(e =>
        {
            ConfigureProvenance(e);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            // Cascade: a title says something only about its work.
            e.HasOne<MediaWorkEntity>().WithMany()
                .HasForeignKey(x => x.MediaWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaWorkIdentifierEntity>(e =>
        {
            ConfigureProvenance(e);
            e.Property(x => x.Scheme).HasConversion<string>().HasMaxLength(32);
            // "Which works claim TMDb 679". Not unique: two works claiming one id is a conflict to
            // show the owner (specification §3.1), not one to forbid and lose.
            e.HasIndex(x => new { x.Scheme, x.Value });
            // Cascade: an identifier says something only about its work.
            e.HasOne<MediaWorkEntity>().WithMany()
                .HasForeignKey(x => x.MediaWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaVersionEntity>(e =>
        {
            ConfigureProvenance(e);
            // Cascade: a version is a version of its work; forgetting the work forgets it (never a
            // file on disk).
            e.HasOne<MediaWorkEntity>().WithMany()
                .HasForeignKey(x => x.MediaWorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PhysicalFileEntity>(e =>
        {
            ConfigureProvenance(e);
            // Groups the names of one inode (hardlinks), which are counted once. Not unique: two
            // names of one file are exactly two rows sharing it.
            e.HasIndex(x => new { x.DeviceId, x.Inode });
            // MAM-7 — Cascade, not SetNull: a row is a name reported for a version, and a name with
            // no version is nothing the core can use. Forgetting a version forgets its names, never a
            // file on disk.
            e.HasOne<MediaVersionEntity>().WithMany()
                .HasForeignKey(x => x.MediaVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            // MAM-8 — SetNull: a location that disappears leaves the file known, just without a
            // library; the next sync decides its location again.
            e.HasOne<LibraryLocationEntity>().WithMany()
                .HasForeignKey(x => x.LibraryLocationId)
                .OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.InspectionStatus).HasConversion<string>().HasMaxLength(32);
            // ADR 0009 — an index and no foreign key, on purpose. SQLite adds a key to a table only by
            // rebuilding it, and EF Core runs that rebuild as three transactions with foreign keys off
            // between them: on an upgrade, where this table holds the library, a stop in between would
            // leave a migration that can't be applied again. The key would guard nothing reachable:
            // a run removes only the readings no name points at (FileInspectionRunner), and a name
            // whose reading is missing reads as having no facts (LibraryMapper). The index serves
            // that "no name points at it" query and the works list's join.
            e.HasIndex(x => x.MediaInspectionId);
        });

        modelBuilder.Entity<LibraryLocationEntity>(e =>
        {
            ConfigureProvenance(e);
        });

        modelBuilder.Entity<LibraryLocationFolderEntity>(e =>
        {
            // MAM-11 — one folder per place in the server's list of a library's folders. Leading with
            // the library, so it also serves the foreign key.
            e.HasIndex(x => new { x.LibraryLocationId, x.Position }).IsUnique();
            // Cascade: a folder says something only about its library. A library a sync no longer
            // reports, or one purged with its integration by ExecuteDelete (LibrarySnapshotStore), takes
            // its folders with it in the database itself, whatever EF tracks.
            e.HasOne<LibraryLocationEntity>().WithMany()
                .HasForeignKey(x => x.LibraryLocationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LibrarySyncRunEntity>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            // MAM-8 — one active run per integration. The service answers 409 first; this settles two
            // starts racing each other, and a finished run never blocks the next one. Partial, so the
            // history of finished runs can hold any number per integration. Named, like the index
            // below, because both lead with the same column.
            e.HasIndex(x => x.IntegrationConnectionId, "IX_LibrarySyncRuns_OneActiveRunPerIntegration")
                .IsUnique()
                .HasFilter("\"Status\" IN ('Queued', 'Running')");
            // "The latest run of each integration", which the Library page asks for on every poll.
            e.HasIndex(x => new { x.IntegrationConnectionId, x.CreatedUtc }, "IX_LibrarySyncRuns_IntegrationConnectionId_CreatedUtc");
            // SetNull: a run is history and outlives the integration; IntegrationName keeps which one.
            e.HasOne<IntegrationConnectionEntity>().WithMany()
                .HasForeignKey(x => x.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
            // SetNull: the run outlives the user who started it; RequestedByUserName keeps who.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AllowedRootEntity>(e =>
        {
            // MAM-9 — a case-sensitive backstop for two saves racing; the rules already refuse one
            // folder listed twice in any letter case a Windows path ignores.
            e.HasIndex(x => x.Path).IsUnique();
        });

        modelBuilder.Entity<MediaInspectionEntity>(e =>
        {
            // ADR 0009 — the cache key of specification §3.3: identity, size and modification time. Unique,
            // so one set of bytes is read once however many names lead to it; a change in any of the
            // four is a new key, and so a new read.
            e.HasIndex(x => new { x.DeviceId, x.Inode, x.SizeBytes, x.ModifiedUtc }).IsUnique();
        });

        modelBuilder.Entity<MediaInspectionTrackEntity>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.DynamicRange).HasConversion<string>().HasMaxLength(32);
            // Cascade: a track is part of its inspection, read and forgotten with it.
            e.HasOne<MediaInspectionEntity>().WithMany()
                .HasForeignKey(x => x.MediaInspectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileInspectionRunEntity>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Trigger).HasConversion<string>().HasMaxLength(32);
            // MAM-9 — at most one queued and one running inspection: unique over the status of the
            // active rows only. A trigger that finds one queued joins it instead of adding another (the
            // service checks first; this settles two triggers racing), and a finished run never blocks
            // the next one.
            e.HasIndex(x => x.Status, "IX_FileInspectionRuns_OneRunPerActiveStatus")
                .IsUnique()
                .HasFilter("\"Status\" IN ('Queued', 'Running')");
            // "The latest finished run", which the Library page asks for on every poll.
            e.HasIndex(x => x.CreatedUtc);
            // SetNull: the run outlives the user who asked for it; RequestedByUserName keeps who.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReleaseEntity>(e =>
        {
            ConfigureProvenance(e);
            e.Property(x => x.PayloadKind).HasConversion<string>().HasMaxLength(32);
            // MAM-12 — "the releases of this search source", which every read of a stored search's list
            // asks for.
            e.HasIndex(x => x.SearchSourceId);
            // SetNull: a release a download job sent outlives its search — it is the job's evidence of
            // what was sent (Restrict, below) — and the prune removes searches by age alone. Adding the
            // key rebuilds this table on upgrade, in three transactions (the ADR 0009 concern that kept
            // PhysicalFiles without one): accepted here because the table holds a day of search results,
            // a few thousand rows at most, so the rebuild is a moment's copy, not the whole library's.
            e.HasOne<SearchSourceEntity>().WithMany()
                .HasForeignKey(x => x.SearchSourceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DownloadJobEntity>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ErrorOrigin).HasConversion<string>().HasMaxLength(32);
            // Restrict: the release snapshot is the job's evidence of what was sent; it can't go
            // while a job points at it.
            e.HasOne<ReleaseEntity>().WithMany()
                .HasForeignKey(x => x.ReleaseId)
                .OnDelete(DeleteBehavior.Restrict);
            // MAM-7 — SetNull, not Restrict: job history outlives the client (DownloadClientName keeps
            // who it was), and Restrict would forbid removing any client that ever had a job —
            // relaxing that later is a SQLite table rebuild.
            e.HasOne<IntegrationConnectionEntity>().WithMany()
                .HasForeignKey(x => x.DownloadClientConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
            // SetNull: the job outlives its destination mapping; DestinationName and
            // ClientDestinationKey keep what was sent.
            e.HasOne<DestinationMappingEntity>().WithMany()
                .HasForeignKey(x => x.DestinationMappingId)
                .OnDelete(DeleteBehavior.SetNull);
            // MAM-13 — SetNull: the job outlives the user who sent it; RequestedByUserName keeps who.
            // Adding the key rebuilds this table on upgrade (the ADR 0009 concern): accepted, as nothing
            // wrote a job before MAM-13, so the rebuild copies an empty table.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            // MAM-13 — one job per request a user sent (ADR 0013): the service answers a repeated request
            // with the job it already made, and this settles two copies of one request racing each other.
            // Led by the user, so it also serves the foreign key; a user's id gone null leaves their jobs'
            // request ids free of each other, as SQLite never counts two nulls the same.
            e.HasIndex(x => new { x.RequestedByUserId, x.RequestId }).IsUnique();
            e.Property(x => x.BlockReasonOrigin).HasConversion<string>().HasMaxLength(32);
            // MAM-14 — "every followed job", which the poller reads every 10 s (DownloadJobTracker). Partial,
            // so it holds the few jobs still followed and never the history, which only grows. Its filter is
            // the core's list (DownloadJobFollowing.FollowedStatuses) rather than a copy of it: a status added
            // to the list changes the model, and the app refuses to start on a model its migrations don't
            // match, so the filter can't drift from the rule unnoticed. Named, as the other partial indexes
            // are.
            e.HasIndex(x => x.Status, "IX_DownloadJobs_Followed")
                .HasFilter($"\"Status\" IN ({string.Join(", ", DownloadJobFollowing.FollowedStatuses.Select(status => $"'{status}'"))})");
            // MAM-14 — "this user's jobs, newest first", the Jobs page's list (a job is its sender's, MAM-14's
            // decision 1); its status filter narrows the same range.
            e.HasIndex(x => new { x.RequestedByUserId, x.CreatedUtc });
        });

        modelBuilder.Entity<DownloadJobFileEntity>(e =>
        {
            // MAM-14 — one file per place in the client's list of a job's files: the poller replaces a job's
            // rows by position, in one save. Leading with the job, so it also serves the foreign key and the
            // files page's "this job's files in order".
            e.HasIndex(x => new { x.DownloadJobId, x.Position }).IsUnique();
            // Cascade: a file row is part of its job, as the client last listed it, and says nothing once the
            // job is gone.
            e.HasOne<DownloadJobEntity>().WithMany()
                .HasForeignKey(x => x.DownloadJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SearchEntity>(e =>
        {
            // MAM-12 — "this user's search", which every read of a stored search checks first.
            e.HasIndex(x => x.UserId);
            // Cascade: a search is its user's, meaningless to anyone else; deleting the user forgets
            // what they searched for.
            e.HasOne<UserEntity>().WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SearchSourceEntity>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.NotSearchedReason).HasConversion<string>().HasMaxLength(32);
            // MAM-12 — "the sources of this search", read with it every time.
            e.HasIndex(x => x.SearchId);
            // Cascade: a source row says something only about its search, and the prune's one
            // ExecuteDelete of old searches takes their sources with them in the database itself.
            e.HasOne<SearchEntity>().WithMany()
                .HasForeignKey(x => x.SearchId)
                .OnDelete(DeleteBehavior.Cascade);
            // SetNull: the search still says which source answered after the integration is removed;
            // Name keeps which one.
            e.HasOne<IntegrationConnectionEntity>().WithMany()
                .HasForeignKey(x => x.IntegrationConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    // The provenance columns of every ProvenancedEntity table (ADR 0006), in one place: the five
    // blocks above would otherwise repeat the same lines, and one of them would drift.
    private static void ConfigureProvenance<TEntity>(EntityTypeBuilder<TEntity> e)
        where TEntity : ProvenancedEntity
    {
        e.Property(x => x.SourceKind).HasConversion<string>().HasMaxLength(32);
        // Re-sync finds a row by the source's own id (MAM-8). Not unique: two assertions may share
        // it. Leading with SourceConnectionId, so it also serves the foreign key.
        e.HasIndex(x => new { x.SourceConnectionId, x.SourceItemId });
        // MAM-7 — SetNull: a fact outlives the integration that reported it; SourceName keeps who
        // that was. MAM-8 decided that a removed library's snapshot is purged, in code by the
        // integration delete (ADR 0006), not by a cascade: every other fact keeps this SetNull.
        e.HasOne<IntegrationConnectionEntity>().WithMany()
            .HasForeignKey(x => x.SourceConnectionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
