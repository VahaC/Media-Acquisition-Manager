using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>MAM-34 — a preparation of the TMDb suggestions end to end, the way the owner's "Prepare again" does it.</summary>
public static class LibraryLinkSuggestionHttpExtensions
{
    private const string RunsUrl = "/api/library/tmdb/runs";

    /// <summary>
    /// Asks for a preparation over HTTP as <paramref name="accessToken"/> (the owner's), runs the worker until its queue
    /// is empty (the test host runs no hosted services) and returns the latest run as the API reads it back. Throws unless
    /// the request is accepted.
    /// </summary>
    public static async Task<LibraryLinkSuggestionRunDto> PrepareLinkSuggestionsAsync(
        this MediaAcquisitionManagerApiFactory factory, string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var start = await factory.PostHttpRequest(RunsUrl, accessToken);
        if (start.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"The TMDb suggestion run request answered {(int)start.StatusCode}: {await start.Content.ReadAsStringAsync(cancellationToken)}");
        }

        await factory.RunQueuedLinkSuggestionsAsync(cancellationToken);
        using var latest = await factory.GetHttpRequest($"{RunsUrl}/latest", accessToken);
        latest.EnsureSuccessStatusCode();
        return await latest.Content.ReadFromJsonAsync<LibraryLinkSuggestionRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("No TMDb suggestion run was found after one was accepted.");
    }

    /// <summary>Runs every preparation queued right now — by a sync, a catalog saved, the reconciler or a request — the
    /// way the worker does in the app.</summary>
    public static Task RunQueuedLinkSuggestionsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.Resolve<LibraryLinkSuggestionRunWorker>().RunQueuedAsync(cancellationToken);
    }
}
