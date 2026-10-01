using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// A library sync the way the Library page makes one (MAM-8): the start over real HTTP, the worker's pass
/// the test host never runs on its own, and the run as the API then reports it.
/// </summary>
public static class LibrarySyncHttpExtensions
{
    /// <summary>
    /// Starts a sync of <paramref name="integrationId"/> as the holder of <paramref name="accessToken"/>,
    /// runs every queued sync through the app's own worker, and returns the run as
    /// <c>GET /api/library/syncs/{id}</c> answers that caller — succeeded or failed, whatever the
    /// adapter's network answered. Arm it first, e.g. with <c>FakeJellyfinServer</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The start was refused: a test that meant to sync
    /// arranged something else, and its later asserts would only mislead.</exception>
    public static async Task<LibrarySyncRunDto> SyncLibraryAsync(
        this MediaAcquisitionManagerApiFactory factory, Guid integrationId, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var start = await factory.PostHttpRequest($"/api/library/sources/{integrationId}/syncs", accessToken);
        if (start.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"The sync start answered {(int)start.StatusCode}: {await start.Content.ReadAsStringAsync(cancellationToken)}");
        }

        await factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);

        using var run = await factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, accessToken);
        run.EnsureSuccessStatusCode();
        return (await run.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
