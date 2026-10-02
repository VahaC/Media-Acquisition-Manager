using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// A search the way the Search page makes one (MAM-12): created over real HTTP, its releases read back as
/// the page reads them — what a Download dialog then sends (MAM-13).
/// </summary>
public static class SearchHttpExtensions
{
    /// <summary>
    /// Searches for <paramref name="text"/> as the holder of <paramref name="accessToken"/> and returns the
    /// first hundred releases the search stored, in the page's default order. Arm the sources first, e.g.
    /// with <c>FakeTorznabServer</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The search or its list was refused: a test that meant to
    /// search arranged something else, and its later asserts would only mislead.</exception>
    public static async Task<IReadOnlyList<SearchReleaseDto>> SearchReleasesAsync(
        this MediaAcquisitionManagerApiFactory factory, string accessToken, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var created = await factory.PostHttpRequest("/api/searches", accessToken, new SearchRequest(text, null, null, null));
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"The search answered {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync(cancellationToken)}");
        }

        var search = (await created.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        using var listed = await factory.GetHttpRequest($"/api/searches/{search.Id}/releases?pageSize=100", accessToken);
        if (listed.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"The search's releases answered {(int)listed.StatusCode}: {await listed.Content.ReadAsStringAsync(cancellationToken)}");
        }

        return (await listed.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
    }
}
