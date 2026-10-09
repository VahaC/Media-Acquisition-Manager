using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="IReplacementSettingsDataFactory"/>
public sealed class ReplacementSettingsDataFactory(ApplicationDbContext db) : IReplacementSettingsDataFactory
{
    public async Task<ReplacementSettingsEntity> Create(bool isEnabled = true)
    {
        var settings = new ReplacementSettingsEntity { IsEnabled = isEnabled };
        db.ReplacementSettings.Add(settings);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return settings;
    }
}
