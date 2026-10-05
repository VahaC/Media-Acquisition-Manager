using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Recommendations;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>MAM-33 — a computation of the recommendations end to end, the way the owner's Recompute now does it.</summary>
public static class RecommendationRunHttpExtensions
{
    private const string RunsUrl = "/api/recommendations/runs";

    /// <summary>
    /// Asks for a computation over HTTP as <paramref name="accessToken"/> (the owner's), runs the worker until its
    /// queue is empty (the test host runs no hosted services) and returns the latest run as the API reads it back.
    /// Throws unless the request is accepted.
    /// </summary>
    public static async Task<RecommendationRunDto> RecomputeRecommendationsAsync(
        this MediaAcquisitionManagerApiFactory factory, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var start = await factory.PostHttpRequest(RunsUrl, accessToken);
        if (start.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"The recommendation run request answered {(int)start.StatusCode}: {await start.Content.ReadAsStringAsync(cancellationToken)}");
        }

        await factory.RunQueuedRecommendationsAsync(cancellationToken);
        return await factory.ReadLatestRecommendationRunAsync(accessToken, cancellationToken)
            ?? throw new InvalidOperationException("No recommendation run was found after one was accepted.");
    }

    /// <summary>Runs every computation queued right now — by a sync, a catalog saved, the reconciler or a request —
    /// the way the worker does in the app.</summary>
    public static Task RunQueuedRecommendationsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<RecommendationRunWorker>().RunQueuedAsync(cancellationToken);
    }

    /// <summary>The latest run as <paramref name="accessToken"/> reads it (<c>GET runs/latest</c>); <c>null</c> when
    /// the API answers 204, before the first run. Throws on any other answer.</summary>
    public static async Task<RecommendationRunDto?> ReadLatestRecommendationRunAsync(
        this MediaAcquisitionManagerApiFactory factory, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var response = await factory.GetHttpRequest($"{RunsUrl}/latest", accessToken);
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RecommendationRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
    }
}
