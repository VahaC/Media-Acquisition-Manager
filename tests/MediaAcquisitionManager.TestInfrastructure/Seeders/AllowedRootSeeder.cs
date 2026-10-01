using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IAllowedRootSeeder"/>
public sealed class AllowedRootSeeder(IServiceProvider services) : SeederBase(services), IAllowedRootSeeder
{
    private IAllowedRootDataFactory Roots => new AllowedRootDataFactory(Db);

    public async Task<IReadOnlyList<AllowedRootEntity>> Allow(params string[] paths)
    {
        var roots = new List<AllowedRootEntity>(paths.Length);
        for (var position = 0; position < paths.Length; position++)
            roots.Add(await Roots.Create(paths[position], position));
        return roots;
    }
}
