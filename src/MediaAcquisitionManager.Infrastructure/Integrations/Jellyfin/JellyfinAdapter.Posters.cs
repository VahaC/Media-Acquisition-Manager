using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;

// MAM-35 — a library work's poster: the item's own primary image, fetched by the app from the Jellyfin the owner saved and
// served to the browser by the app (ADR 0027), so the browser never talks to Jellyfin and the key never leaves the server.
//
// What it stands on was recorded live on Jellyfin 12.1.0 on 2026-10-07 (VendorSamples/Jellyfin/image-primary*-response.txt):
// GET /Items/{id}/Images/Primary answers without a key; with format=Jpg, maxWidth=342 and quality=90 a 342x513 poster is a
// JPEG of 35 KB (115 KB without quality=90; without format=Jpg the server picks WebP when the Accept header allows it). The
// tag is not checked — a wrong one answers the same image and echoes the asked tag as its ETag —, so it only versions the
// URL, and the sync's tag is what says which picture is current. An item without the image answers 404 with a JSON string,
// and an id Jellyfin doesn't know 400 text/plain. Not every primary image is a poster: an episode's (Tom & Jerry Kids,
// 342x237) or an episode listed as a film (Blitzkrieg, 1920x1080 after its Identify) is a frame of the video.
internal sealed partial class JellyfinAdapter
{
    private static readonly Message NotAJpegError = Message.Of("jellyfin.poster.not-jpeg");

    private static readonly Message NoPosterError = Message.Of("jellyfin.poster.none");

    /// <summary>
    /// The widest a poster is asked for: TMDb's <c>w342</c>, the size of the app's other posters (MAM-33), which fills the
    /// card's 6rem box on a display of twice the density. Jellyfin keeps the image's own shape within it.
    /// </summary>
    private const int PosterMaximumWidth = 342;

    /// <summary>
    /// Jellyfin's JPEG quality for the poster: recorded live, 90 makes a 342-wide poster 35 KB where Jellyfin's own default
    /// made it 115 KB, with no difference a 6rem card shows.
    /// </summary>
    private const int PosterQuality = 90;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>GET /Items/{id}/Images/Primary?tag={tag}&amp;format=Jpg&amp;maxWidth=342&amp;quality=90</c> asking for a JPEG, with
    /// the key in the <c>Authorization</c> header like every other call: the route needs none, but the key only ever goes
    /// to the address the owner saved (ADR 0007), and a reverse proxy in front of Jellyfin may want it. The id must be a
    /// Guid — Jellyfin's ids are, and it goes into the path in Jellyfin's 32-hex-digit form (MAM-8's rule) — and the tag
    /// one the sync would read (<see cref="PosterTagPattern"/>); either otherwise is the caller's mistake and nothing is sent.
    /// The body is buffered whole within the shared client's limit and kept only as a JPEG. 404 and 400 are both
    /// <see cref="IntegrationCallStatus.NotFound"/>: Jellyfin answers 404 for an item without the image and 400 for an id
    /// it doesn't know (recorded), and either way there is no poster to show. On the shared client, never Apply's: a
    /// poster is answered in milliseconds.
    /// </remarks>
    public Task<IntegrationCallResult<LibraryPoster>> GetPosterAsync(
        IntegrationConnectionSettings settings, string sourceItemId, string posterTag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourceItemId);
        ArgumentNullException.ThrowIfNull(posterTag);
        if (!Guid.TryParse(sourceItemId, out var itemId))
            throw new ArgumentException("An item id is one Jellyfin gives, such as bad3e58c7b81156c7bccd6235599accd.", nameof(sourceItemId));
        if (!PosterTagPattern().IsMatch(posterTag))
            throw new ArgumentException("A poster tag is one Jellyfin gives, such as 7060953c5bc967a2e947ecc514b77a37.", nameof(posterTag));

        // Only the parsed id and the checked tag come from outside; the size, format and quality are this class's.
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"{ItemPath(itemId)}/Images/Primary?tag={posterTag}&format=Jpg&maxWidth={PosterMaximumWidth}&quality={PosterQuality}");
        return CallAsync<LibraryPoster>(
            settings,
            async (client, sessionSecrets, token) =>
            {
                using var request = PosterRequest(settings, path);
                using var response = await client.SendAsync(request, token);
                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await ReadPosterAsync(response, token),
                    // 404: the item has no primary image (a virtual special); 400: Jellyfin no longer knows the id, its answer
                    // to a well-formed id it doesn't have (recorded) — the item was removed since the sync read it.
                    HttpStatusCode.NotFound or HttpStatusCode.BadRequest =>
                        IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.NotFound, NoPosterError),
                    // Jellyfin itself never refuses the image route; a proxy in front of it may, as it would any call.
                    HttpStatusCode.Unauthorized => IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.AuthenticationFailed, KeyRefusedError),
                    HttpStatusCode.Forbidden => IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.AuthenticationFailed, LibraryForbiddenError),
                    // A redirect is reported, never followed (the handler doesn't), like every other answer.
                    _ => DescribeUnexpectedStatus<LibraryPoster>(response, settings, sessionSecrets),
                };
            },
            cancellationToken);
    }

    /// <summary>The keyed GET of the poster, asking for a JPEG only: <see cref="CreateRequest"/> asks for JSON, which this
    /// answer never is.</summary>
    private static HttpRequestMessage PosterRequest(IntegrationConnectionSettings settings, string path)
    {
        var request = CreateRequest(settings, path);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));
        return request;
    }

    /// <summary>The poster's bytes when the answer says it is a JPEG and has a body; anything else is not a poster, whatever
    /// its bytes are.</summary>
    private static async Task<IntegrationCallResult<LibraryPoster>> ReadPosterAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            return IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAJpegError);

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return content.Length > 0
            ? IntegrationCallResult<LibraryPoster>.Succeeded(new LibraryPoster(content, "image/jpeg"))
            : IntegrationCallResult<LibraryPoster>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAJpegError);
    }
}
