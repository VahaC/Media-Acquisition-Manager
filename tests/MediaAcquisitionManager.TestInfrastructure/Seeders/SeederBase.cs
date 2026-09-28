using Microsoft.Extensions.DependencyInjection;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>
/// Base for self-contained seeders. Given the test's DI scope, a seeder resolves everything it needs
/// itself (DbContext, encryption, token service, per-entity data factories) — nothing is threaded in
/// on the constructor beyond the scope.
/// </summary>
public abstract class SeederBase(IServiceProvider services)
{
    protected IServiceProvider Services => services;

    protected ApplicationDbContext Db => services.GetRequiredService<ApplicationDbContext>();

    protected T Resolve<T>() where T : notnull => services.GetRequiredService<T>();
}
