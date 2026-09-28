using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Persistence;

/// <summary>
/// The app's one EF Core context over the SQLite database. Controllers and services take it
/// directly — there is no repository layer — and the tests run it against a real SQLite file.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Empty in MAM-1 on purpose: the first entity arrives with the ticket that needs it, and
        // each entity then gets its own modelBuilder.Entity<T> block here (string enums, indexes,
        // delete behaviour with a comment on why).
    }
}
