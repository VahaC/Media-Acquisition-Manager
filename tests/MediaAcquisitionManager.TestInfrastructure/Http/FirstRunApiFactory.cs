using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// The first start of a fresh installation: an empty data directory (no database, no
/// <c>.secrets</c>) and no configured secrets, so the app has to create the database, migrate it
/// and provision both secrets itself.
/// </summary>
public sealed class FirstRunApiFactory : MediaAcquisitionManagerApiFactory
{
    protected override bool StartFromMigratedDatabase => false;

    protected override void ConfigureSecrets(IWebHostBuilder builder)
    {
        // Explicitly blank rather than left out: settings arrive as command-line arguments, which
        // outrank every other source, so a key set in a developer's environment cannot turn
        // provisioning off and make the test pass for the wrong reason.
        builder.UseSetting(FirstRunSecrets.EncryptionKey.ConfigurationKey, "");
        builder.UseSetting(FirstRunSecrets.JwtSecret.ConfigurationKey, "");
    }
}
