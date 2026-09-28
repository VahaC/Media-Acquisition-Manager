using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IInstallationDataFactory"/>
public sealed class InstallationDataFactory(ApplicationDbContext db, TimeProvider time) : IInstallationDataFactory
{
    public async Task<InstallationEntity> SetOwner(Guid ownerUserId)
    {
        // One row at most, like the app keeps it: a second owner replaces the first instead of adding
        // a row the fixed key would refuse.
        var installation = await db.Installations.SingleOrDefaultAsync();
        if (installation is null)
        {
            installation = new InstallationEntity { SetupCompletedUtc = time.GetUtcNow().UtcDateTime };
            db.Installations.Add(installation);
        }

        installation.OwnerUserId = ownerUserId;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return installation;
    }
}
