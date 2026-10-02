using MediaAcquisitionManager.Api.Services.Downloads;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>The download jobs' following, driven by hand (MAM-14): the test host runs no hosted services, so a
/// test runs the tracker's passes itself, one at a time.</summary>
public static class DownloadJobTrackingHttpExtensions
{
    /// <summary>
    /// Runs one pass of the app's own <see cref="DownloadJobTracker"/> over the followed jobs — a scope of its
    /// own, every client asked as the running app asks it — and returns once it is over. Arm the clients first,
    /// e.g. with <c>FakeQBittorrentServer</c>; an unarmed one answers 503.
    /// </summary>
    public static Task PollDownloadJobsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<DownloadJobTracker>().PollOnceAsync(cancellationToken);
    }
}
