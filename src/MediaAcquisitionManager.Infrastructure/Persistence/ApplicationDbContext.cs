using MediaAcquisitionManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

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
    }
}
