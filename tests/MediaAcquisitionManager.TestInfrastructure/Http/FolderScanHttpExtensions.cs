using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>A scan of the media folders end to end, the way the owner's Scan folders does it (MAM-18).</summary>
public static class FolderScanHttpExtensions
{
    /// <summary>
    /// Asks for a scan over HTTP as <paramref name="accessToken"/>, runs the worker until its queue is empty
    /// (the test host runs no hosted services) and returns the run as the API reads it back. The file
    /// inspection the scan queues is left queued: run it with <c>RunQueuedInspectionsAsync</c>. Throws
    /// unless the request is accepted.
    /// </summary>
    public static async Task<FolderScanRunDto> ScanFoldersAsync(
        this MediaAcquisitionManagerApiFactory factory, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var start = await factory.PostHttpRequest("/api/library/scans", accessToken);
        if (start.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"The scan request answered {(int)start.StatusCode}: {await start.Content.ReadAsStringAsync(cancellationToken)}");
        }

        await factory.RunQueuedScansAsync(cancellationToken);

        using var run = await factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, accessToken);
        run.EnsureSuccessStatusCode();
        return (await run.Content.ReadFromJsonAsync<FolderScanRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>Runs every scan queued right now — by a request or the reconciler — the way the worker does
    /// in the app.</summary>
    public static Task RunQueuedScansAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<FolderScanWorker>().RunQueuedAsync(cancellationToken);
    }
}
