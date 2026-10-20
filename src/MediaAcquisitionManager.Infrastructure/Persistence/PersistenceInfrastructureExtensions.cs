using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Infrastructure.Persistence;

/// <summary>
/// The SQLite registration behind <see cref="DependencyInjection.AddMediaAcquisitionManagerPersistence"/>,
/// plus the one options recipe that the app, <c>dotnet ef</c> and the test databases all share — so a
/// test database can never be built with a different migrations assembly than the app uses.
/// </summary>
internal static class PersistenceInfrastructureExtensions
{
    /// <summary>
    /// Migrations live in the Api project (the EF startup project), not beside the context; without
    /// naming it, EF looks in this assembly, finds nothing and migrates to an empty schema.
    /// </summary>
    public const string MigrationsAssembly = "MediaAcquisitionManager.Api";

    public static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services, string databasePath)
    {
        // Stateless, so one instance serves every context.
        services.AddSingleton<SqlitePragmaInterceptor>();

        services.AddDbContext<ApplicationDbContext>((provider, options) => options
            .UseMediaAcquisitionManagerSqlite(databasePath)
            .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>())
            // Every ISaveChangesInterceptor a layer above registers is attached here, so a save
            // observer (an audit that cannot be bypassed, say) needs no change in this project.
            .AddInterceptors(provider.GetServices<ISaveChangesInterceptor>()));

        return services;
    }

    /// <summary>SQLite at <paramref name="databasePath"/> with the Api migrations assembly. Generic
    /// so a caller holding a <c>DbContextOptionsBuilder&lt;ApplicationDbContext&gt;</c> keeps it.</summary>
    public static TBuilder UseMediaAcquisitionManagerSqlite<TBuilder>(this TBuilder options, string databasePath)
        where TBuilder : DbContextOptionsBuilder
    {
        options.UseSqlite(ConnectionStringFor(databasePath), sqlite => sqlite.MigrationsAssembly(MigrationsAssembly));
        return options;
    }

    /// <summary>
    /// Built with the connection-string builder rather than concatenated, so a data directory whose
    /// path contains <c>;</c> or <c>=</c> is quoted instead of silently cutting the string short.
    /// Internal for the test databases: Microsoft.Data.Sqlite keys its connection pools by this exact
    /// string, and they close one database's pool by it.
    /// </summary>
    internal static string ConnectionStringFor(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
}
