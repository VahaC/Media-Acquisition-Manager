using MediaAcquisitionManager.Api.Services.Replacements;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>The move of the verified replacements' old copies into the quarantine, driven by hand (MAM-26): the test host
/// runs no hosted services, so a test runs the mover's passes itself, one at a time.</summary>
public static class ReplacementQuarantineHttpExtensions
{
    /// <summary>
    /// Runs one pass of the app's own <see cref="ReplacementQuarantineTracker"/> — a scope of its own, the cut jobs
    /// settled, the verified ones moved, the notices due sent, as the running app does — and returns once it is over.
    /// Arm the fakes first (the files and the quarantine, qBittorrent, Jellyfin); an unarmed service answers 503.
    /// </summary>
    public static Task QuarantineReplacementsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<ReplacementQuarantineTracker>().RunOnceAsync(cancellationToken);
    }
}
