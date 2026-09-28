using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediaAcquisitionManager.Api.Persistence;

/// <summary>
/// How <c>dotnet ef</c> builds the context — from the default storage layout, without booting the
/// app through <c>Program.Main</c>. The tools still fall back to Main when <c>--context</c> names a
/// context this factory doesn't build; Main then skips its data-directory and secrets side effects
/// because <c>EF.IsDesignTime</c> is set.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // The path only matters to `dotnet ef database update`, which nobody needs to run: the app
        // migrates itself on startup. `migrations add` never opens the database. The tools run with
        // the startup project as the working directory, so this is the default Data directory.
        var storage = StorageLayout.Resolve(new StorageOptions(), Directory.GetCurrentDirectory());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMediaAcquisitionManagerSqlite(storage.DatabasePath)
            .Options;
        return new ApplicationDbContext(options);
    }
}
