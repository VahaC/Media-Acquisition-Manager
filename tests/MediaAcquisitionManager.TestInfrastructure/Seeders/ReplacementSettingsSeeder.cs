using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IReplacementSettingsSeeder"/>
public sealed class ReplacementSettingsSeeder(IServiceProvider services) : SeederBase(services), IReplacementSettingsSeeder
{
    private IReplacementSettingsDataFactory Settings => new ReplacementSettingsDataFactory(Db);

    private IReplacementFolderDataFactory Folders => new ReplacementFolderDataFactory(Db);

    public async Task<IReadOnlyList<ReplacementFolderEntity>> SwitchOn(params string[] folders)
    {
        await Settings.Create(isEnabled: true);
        return await Tick(folders);
    }

    public async Task<IReadOnlyList<ReplacementFolderEntity>> Tick(params string[] folders)
    {
        var ticks = new List<ReplacementFolderEntity>(folders.Length);
        for (var position = 0; position < folders.Length; position++)
            ticks.Add(await Folders.Create(folders[position], position));
        return ticks;
    }
}
