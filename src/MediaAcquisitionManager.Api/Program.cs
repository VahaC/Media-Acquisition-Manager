using MediaAcquisitionManager.Api.Extensions;
using MediaAcquisitionManager.Api.Health;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Infrastructure;
using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api;

/// <summary>
/// The entry point. A classic <c>Program</c> class rather than top-level statements, so the test
/// host can boot the real app through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // MAM_Section__Key reaches Section:Key, e.g. MAM_Storage__DataDirectory.
        builder.Configuration.AddEnvironmentVariables(prefix: "MAM_");
        // The prefixed variables were added above the command line; put the command line back on
        // top, where the default builder keeps it. An explicit --Storage:DataDirectory must beat a
        // MAM_ variable left in a shell — and the test host passes its settings as arguments, so
        // without this a developer's MAM_Storage__DataDirectory would point a test run (which wipes
        // tables) at their real database.
        builder.Configuration.AddCommandLine(args);

        // Every path is resolved once, before anything opens a file; the data directory must exist
        // before SQLite creates the database in it.
        var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
            ?? new StorageOptions();
        var storage = StorageLayout.Resolve(storageOptions, builder.Environment.ContentRootPath);

        // DesignTimeDbContextFactory alone doesn't keep `dotnet ef` out of Main: when --context names
        // no context a factory builds (`-c Release`, meant as the configuration, does that), the tools
        // run Main up to builder.Build() to look for contexts in DI. A design-time command must not
        // create a data directory or write secrets.
        if (!EF.IsDesignTime)
        {
            Directory.CreateDirectory(storage.DataDirectory);

            // Blank secrets are generated once into <data>/.secrets and added on top of configuration.
            // Last on purpose: it can only see a value an earlier source provides.
            builder.Configuration.AddMediaAcquisitionManagerFirstRunSecrets(storage.SecretsDirectory);
        }

        builder.Services.AddSectionOptions<StorageOptions>(builder.Configuration);
        builder.Services.AddMediaAcquisitionManagerApi();
        builder.Services.TryAddSingletonTimeProvider();
        builder.Services.AddMediaAcquisitionManagerInfrastructure(storage);
        builder.Services.AddMediaAcquisitionManagerHealthChecks();

        var app = builder.Build();
        app.ApplyDatabaseMigrations();

        app.UseExceptionHandler();
        app.UseSecurityHeaders();
        app.UseStaticFiles();
        // Includes UnknownApiController: an /api URL nothing else claims answers 404 { error }
        // instead of falling through to the SPA shell below.
        app.MapControllers();
        app.MapMediaAcquisitionManagerHealthEndpoints();
        // Client-side routes (/library, /settings/…) get the SPA shell; React Router takes it from there.
        app.MapFallbackToFile("index.html");

        app.Run();
        return 0;
    }
}
