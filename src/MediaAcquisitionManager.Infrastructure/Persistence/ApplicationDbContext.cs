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

    public DbSet<LibrarySyncRunEntity> LibrarySyncRuns => Set<LibrarySyncRunEntity>();

    public DbSet<ReleaseEntity> Releases => Set<ReleaseEntity>();

    public DbSet<DownloadJobEntity> DownloadJobs => Set<DownloadJobEntity>();

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
        });

        modelBuilder.Entity<LibraryLocationEntity>(e =>
        {
            ConfigureProvenance(e);
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

        modelBuilder.Entity<ReleaseEntity>(e =>
        {
            ConfigureProvenance(e);
            e.Property(x => x.PayloadKind).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<DownloadJobEntity>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
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
