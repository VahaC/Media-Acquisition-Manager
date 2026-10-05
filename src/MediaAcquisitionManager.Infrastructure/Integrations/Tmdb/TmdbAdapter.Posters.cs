using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

// MAM-33 — a poster, fetched by the app from TMDb's image host and served to the browser by the app, so
// the browser is never sent to a third party and the image host never sees who looks.
internal sealed partial class TmdbAdapter
{
    private const string NotAnImageError =
        "TMDb's image host answered the poster with something other than a JPEG or PNG image.";

    /// <summary>The media types a poster is kept as: what TMDb serves posters as, and nothing the
    /// browser could run.</summary>
    private static readonly string[] PosterMediaTypes = ["image/jpeg", "image/png"];

    /// <summary>MAM-33 — TMDb's image host, <c>https://image.tmdb.org</c>: the address a poster's failure
    /// names.</summary>
    private static readonly string PosterHost = new Uri(PosterBaseUrl).GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// <c>GET https://image.tmdb.org/t/p/w342{posterPath}</c>, without the token: the image host takes
    /// none, and a credential goes only to the address the owner saved (ADR 0007). The path must be one
    /// TMDb gives (<see cref="PosterPathPattern"/>); any other is the caller's mistake and nothing is
    /// sent. The body is buffered whole within the client's limit (4 MiB; a w342 poster is tens of
    /// kilobytes) and kept only as JPEG or PNG.
    /// </summary>
    public Task<IntegrationCallResult<CatalogImage>> GetPosterAsync(
        IntegrationConnectionSettings settings, string posterPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(posterPath);
        if (!PosterPathPattern().IsMatch(posterPath))
            throw new ArgumentException("A poster path is one TMDb gives, such as /esaQNJKjE8IDk5G0tmEOJL7AaEP.jpg.", nameof(posterPath));

        // The host and size are this class's constants; only the checked file name comes from outside.
        var uri = new Uri(PosterBaseUrl + PosterSize + posterPath);
        // MAM-33 — a transport failure or a timeout names the address it happened at (HttpIntegrationAdapter): the image
        // host's, never the API address the owner saved, which this request never went to. The secrets stay the
        // connection's, so whatever is said is still masked of them.
        var posterSettings = settings with { BaseUrl = PosterHost };
        return CallAsync<CatalogImage>(
            posterSettings,
            async (client, sessionSecrets, token) =>
            {
                using var response = await SendAsync(client, () => PosterRequest(uri), token);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await ReadPosterAsync(response, token),
                    // The path passed the pattern above, so it is safe to repeat.
                    HttpStatusCode.NotFound => IntegrationCallResult<CatalogImage>.Failed(
                        IntegrationCallStatus.NotFound, $"TMDb's image host has no poster at {posterPath}."),
                    HttpStatusCode.TooManyRequests => IntegrationCallResult<CatalogImage>.Failed(IntegrationCallStatus.Refused, TooManyRequestsError),
                    _ => DescribeUnexpectedStatus<CatalogImage>(response, settings, sessionSecrets),
                };
            },
            cancellationToken);
    }

    /// <summary>A GET of the poster asking for an image — and no <c>Authorization</c> header.</summary>
    private static HttpRequestMessage PosterRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        return request;
    }

    /// <summary>The poster's bytes when the answer says it is a JPEG or a PNG and has a body; anything
    /// else is not a poster, whatever its bytes are.</summary>
    private static async Task<IntegrationCallResult<CatalogImage>> ReadPosterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = PosterMediaTypes.FirstOrDefault(type =>
            string.Equals(type, response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase));
        if (mediaType is null)
            return IntegrationCallResult<CatalogImage>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAnImageError);

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return content.Length > 0
            ? IntegrationCallResult<CatalogImage>.Succeeded(new CatalogImage(content, mediaType))
            : IntegrationCallResult<CatalogImage>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAnImageError);
    }
}
