using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Files;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>An inspection of the library's files end to end, the way the owner's Inspect files does it
/// (MAM-9).</summary>
public static class FileInspectionHttpExtensions
{
    /// <summary>
    /// Asks for an inspection over HTTP as <paramref name="accessToken"/>, runs the worker until its queue
    /// is empty (the test host runs no hosted services) and returns the run as the API reads it back.
    /// Throws unless the request is accepted.
    /// </summary>
    public static async Task<FileInspectionRunDto> InspectFilesAsync(
        this MediaAcquisitionManagerApiFactory factory, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var start = await factory.PostHttpRequest("/api/library/inspections", accessToken);
        if (start.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"The inspection request answered {(int)start.StatusCode}: {await start.Content.ReadAsStringAsync(cancellationToken)}");
        }

        await factory.RunQueuedInspectionsAsync(cancellationToken);

        using var run = await factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, accessToken);
        run.EnsureSuccessStatusCode();
        return (await run.Content.ReadFromJsonAsync<FileInspectionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>Runs every inspection queued right now — by a sync, the reconciler or a request — the way
    /// the worker does in the app.</summary>
    public static Task RunQueuedInspectionsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<FileInspectionWorker>().RunQueuedAsync(cancellationToken);
    }
}
