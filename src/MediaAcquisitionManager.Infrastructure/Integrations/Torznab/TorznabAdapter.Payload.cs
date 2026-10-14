using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

// MAM-10 — the payload fetch: a magnet is the payload as it is; a download link is fetched from the
// feed's own origin only, once, following no redirect (ADR 0007), and its answer is a .torrent file, a
// magnet the feed redirects to, or a sentence.
internal sealed partial class TorznabAdapter
{
    /// <summary>The longest scheme a sentence repeats: real ones are a few letters.</summary>
    private const int SchemeMaximumLength = 20;

    private const string NotATorrentError =
        "The Torznab feed answered the download with something that isn't a .torrent file.";

    private const string ElsewhereError =
        "The release's download link points somewhere other than the Torznab feed's address, so the app didn't fetch it.";

    private const string KeyMissingError =
        "The release's download link needs the Torznab feed's API key, and none is saved for the feed. Enter its API key.";

    // Jackett answers every failed download with a bare 404; Prowlarr with an <error> (FeedError).
    private const string DownloadNotFoundError =
        "The Torznab feed couldn't hand over this release (404 Not Found): it may be gone, or the feed failed to fetch it; its own log says which.";

    private const string RedirectWithoutTargetError =
        "The Torznab feed answered the download with a redirect that doesn't say where to, and the app doesn't follow redirects.";

    /// <inheritdoc/>
    public Task<IntegrationCallResult<ReleasePayload>> FetchPayloadAsync(
        IntegrationConnectionSettings settings, ReleasePayloadLocator locator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(locator.Value, nameof(locator));

        // A magnet is the payload itself: nothing to fetch, and nothing of the feed's in it.
        if (TorznabLinks.IsMagnet(locator.Value))
            return Task.FromResult(IntegrationCallResult<ReleasePayload>.Succeeded(new ReleasePayload(ReleasePayloadKind.Magnet, null, locator.Value)));

        return CallAsync<ReleasePayload>(
            settings, (client, sessionSecrets, token) => FetchAsync(client, settings, locator.Value, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<ReleasePayload>> FetchAsync(
        HttpClient client, IntegrationConnectionSettings settings, string locator, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        if (TorznabLinks.WithApiKey(locator, settings.ApiKey) is not { } link)
            return IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.AuthenticationFailed, KeyMissingError);

        // MAM-10 — the key goes only where the owner sent it: the feed's own scheme, host and port. A
        // locator is one this connection listed, but a stored row can be edited or swapped, and the
        // request carries the key; anywhere else, nothing is sent at all.
        var feed = new Uri(EndpointFor(settings.BaseUrl));
        if (!TorznabLinks.TryParseWebLink(link, out var uri) || !TorznabLinks.SameOrigin(uri, feed))
            return IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.UnexpectedResponse, ElsewhereError);

        // Prowlarr's link= token is a credential of its own (it opens the tracker's download), and the
        // feed's errors can echo any part of the link: each is masked like the key.
        foreach (var secret in TorznabLinks.SecretsOf(uri, link))
            sessionSecrets.Add(secret);

        // The very instance checked above: re-parsing a string could yield another address.
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-bittorrent"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await client.SendAsync(request, cancellationToken);
        // Buffered by the client within its limit (MaximumResponseBytes); a .torrent is far smaller.
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        // The feed's text is masked of the link's path segments too; the finished sentence isn't, or
        // Prowlarr's /1/download would mask the word "download" out of it (TorznabLinks.PathSegmentsOf).
        string?[] secrets = [.. CallSecrets(settings, sessionSecrets), .. TorznabLinks.PathSegmentsOf(uri)];

        // Prowlarr reports a failed download as <error code=… description=…/> with 410, 429 or 500.
        using (var stream = new MemoryStream(body, writable: false))
        {
            if (TryReadXml(stream)?.Root is { Name.LocalName: "error" } error)
                return DescribeError<ReleasePayload>(error, response.StatusCode, secrets, FeedRequest.Download);
        }

        if ((int)response.StatusCode is >= 300 and < 400)
            return Redirected(response, feed, secrets);

        return response.StatusCode switch
        {
            HttpStatusCode.OK => IsTorrentFile(body)
                ? IntegrationCallResult<ReleasePayload>.Succeeded(new ReleasePayload(ReleasePayloadKind.TorrentFile, body, null))
                : IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.UnexpectedResponse, NotATorrentError),
            // Jackett's answer to a wrong jackett_apikey in a download link, as Prowlarr's to a wrong apikey.
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => IntegrationCallResult<ReleasePayload>.Failed(
                IntegrationCallStatus.AuthenticationFailed, RefusedKeySentence(StatusText(response))),
            HttpStatusCode.NotFound => IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.NotFound, DownloadNotFoundError),
            HttpStatusCode.Gone => IntegrationCallResult<ReleasePayload>.Failed(
                IntegrationCallStatus.NotFound, $"The Torznab feed can't hand over this release any more ({StatusText(response)}). Search again."),
            HttpStatusCode.TooManyRequests => IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.Refused, TooManyRequestsError),
            _ => DescribeUnexpectedStatus<ReleasePayload>(response, settings, sessionSecrets),
        };
    }

    /// <summary>
    /// A 3xx to the download: a magnet is the payload — Prowlarr and Jackett redirect to it for an
    /// indexer that gives magnets only — and anything else is refused without a second request. Not
    /// even a redirect within the feed's own address is followed: ADR 0007 rejects that for a request
    /// that carries the key, and following one is a later option, not this ticket's.
    /// </summary>
    private static IntegrationCallResult<ReleasePayload> Redirected(HttpResponseMessage response, Uri feed, string?[] secrets)
    {
        if (!response.Headers.NonValidated.TryGetValues("Location", out var values) || values.Count != 1)
            return IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.UnexpectedResponse, RedirectWithoutTargetError);

        // The typed header parses the value; one it can't parse (a magnet with a space in its name, say)
        // is still there as sent.
        var location = response.Headers.Location?.OriginalString ?? values.First();
        if (location.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) && TorznabLinks.IsMagnet(location))
            return IntegrationCallResult<ReleasePayload>.Succeeded(new ReleasePayload(ReleasePayloadKind.Magnet, null, location));

        // Scheme, host and port only: the path or the query of a tracker's link can hold a passkey.
        if (response.RequestMessage?.RequestUri is { } requestUri
            && Uri.TryCreate(requestUri, location, out var target)
            && target.Scheme is "http" or "https")
        {
            var origin = RemoteText.Clean(TorznabLinks.Origin(target), OriginMaximumLength, secrets);
            return IntegrationCallResult<ReleasePayload>.Failed(
                IntegrationCallStatus.UnexpectedResponse,
                TorznabLinks.SameOrigin(target, feed)
                    ? $"The Torznab feed sends this download on to another address of its own at {origin} instead of handing it over, and the app doesn't follow redirects."
                    : $"The Torznab feed sends this download on to {origin} instead of handing it over, and the app doesn't follow it. Switch off the feed's option to redirect downloads to the tracker.");
        }

        if (SchemePattern().Match(location) is { Success: true } scheme)
        {
            var name = RemoteText.Clean(scheme.Groups[1].Value, SchemeMaximumLength, secrets);
            return IntegrationCallResult<ReleasePayload>.Failed(
                IntegrationCallStatus.UnexpectedResponse,
                $"The Torznab feed sends this download on to a “{name}” link instead of handing it over, and the app doesn't follow it.");
        }

        return IntegrationCallResult<ReleasePayload>.Failed(IntegrationCallStatus.UnexpectedResponse, RedirectWithoutTargetError);
    }

    /// <summary>
    /// Whether <paramref name="body"/> is a bencoded torrent: a dictionary (<c>d</c> … <c>e</c>, trailing
    /// ASCII whitespace allowed) holding an <c>info</c> key. Prowlarr passes the tracker's bytes through
    /// unchanged, so a login page or an error page in HTML is what this tells apart.
    /// </summary>
    private static bool IsTorrentFile(byte[] body)
    {
        var end = body.Length;
        while (end > 0 && body[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r')
            end--;

        var torrent = body.AsSpan(0, end);
        return torrent.Length >= 2 && torrent[0] == (byte)'d' && torrent[^1] == (byte)'e' && torrent.IndexOf("4:info"u8) >= 0;
    }

    /// <summary>A URI scheme at the start of a value (RFC 3986): a letter, then letters, digits, <c>+ - .</c>.</summary>
    [GeneratedRegex(@"^([a-z][a-z0-9+.\-]*):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();
}
