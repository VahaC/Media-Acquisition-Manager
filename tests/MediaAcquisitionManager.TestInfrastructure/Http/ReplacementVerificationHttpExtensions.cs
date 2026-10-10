using MediaAcquisitionManager.Api.Services.Replacements;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>The check of the replacements' new files, driven by hand (MAM-25): the test host runs no hosted services, so a
/// test runs the verifier's passes itself, one at a time.</summary>
public static class ReplacementVerificationHttpExtensions
{
    /// <summary>
    /// Runs one pass of the app's own <see cref="ReplacementVerificationTracker"/> over the replacements being checked — a
    /// scope of its own, the client, the disk and the library read as the running app reads them — and returns once it is
    /// over. Arm the fakes first (qBittorrent, Jellyfin, the files); an unarmed service answers 503.
    /// </summary>
    public static Task VerifyReplacementsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<ReplacementVerificationTracker>().RunOnceAsync(cancellationToken);
    }
}
