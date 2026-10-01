using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

// MAM-10 — the links a release is shown with: its page link and its id, stored in clear and printed by
// the hit's ToString, and the addresses a feed's error quotes.
internal static partial class TorznabLinks
{
    /// <summary>What a release's page link column holds (<c>ReleaseEntity.SourcePageUrl</c>).</summary>
    private const int PageLinkMaximumLength = 2048;

    /// <summary>What a provenanced row's source id column holds (<c>ProvenancedEntity.SourceItemId</c>): a
    /// page link longer than this is an id only as its digest.</summary>
    private const int ItemIdMaximumLength = 512;

    /// <summary>
    /// The URL parameters that name a credential: the list <c>VendorSamplesTests</c> keeps the samples
    /// clean of (the collector's, MAM-6).
    /// </summary>
    private static readonly HashSet<string> TextOnlyMaskedNames = new(["sid", "tr", "ws", "xs", "as", "url"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> KeyLikeNames = new(
        [
            "apikey", "api_key", "api_token", "apitoken", "jackett_apikey", "passkey", "pk", "uk", "tp", "token",
            "access_token", "link", "authkey", "torrent_pass", "rsskey", "key", "secret", "password", "passwd", "pwd",
            "pass", "auth", "sig", "signature", "x-plex-token",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The page link of a release as it may be stored and shown, or <c>null</c>: an absolute http or
    /// https link without user info, its fragment and every <c>sid</c> pair removed; refused when what
    /// remains names a credential (<see cref="CarriesCredential"/>), looks like a download link rather
    /// than a page (<see cref="LooksLikeDownload"/>), points at the feed itself — a gateway's address,
    /// not a page, and the owner's LAN address shown to every member — or is longer than its column.
    /// </summary>
    public static string? PageLink(string? text, Uri feed, IReadOnlySet<string> locatorValues, string?[] secrets)
    {
        if (!TryParseWebLink(text?.Trim(), out var uri) || WithoutSession(uri) is not { } link)
            return null;

        return link.AbsoluteUri.Length <= PageLinkMaximumLength
            && !CarriesCredential(link, locatorValues, secrets)
            && !LooksLikeDownload(link)
            && !SameOrigin(link, feed)
                ? link.AbsoluteUri
                : null;
    }

    /// <summary>
    /// The listing's id as it may be stored in clear (MAM-10): a <c>guid</c> that is a web link is kept,
    /// without its session, only when it is the release's own page link (<paramref name="pageLink"/>,
    /// already cleared to be shown) — Toloka's <c>https://toloka.to/t699400</c>; a plain token is kept
    /// when it holds no secret. Anything else — a magnet, Prowlarr's guid that is the tracker's own
    /// download link with the passkey in its path, an address that doesn't parse — becomes
    /// <c>sha256:</c> and the hex digest of it, stable across searches so the release is still known
    /// again. No guid at all falls back to <paramref name="pageLink"/>.
    /// </summary>
    /// <remarks>
    /// The page link is the only web link trusted in clear: a passkey in a link's path looks like any
    /// other path segment (<c>/rss/download/123/&lt;passkey&gt;/x.torrent</c>), and the id only needs to
    /// be stable, not readable.
    /// </remarks>
    public static string? ItemId(string? text, string? pageLink, string?[] secrets)
    {
        var id = text?.Trim();
        if (string.IsNullOrEmpty(id))
            return pageLink is { Length: > ItemIdMaximumLength } ? Digest(pageLink) : pageLink;

        if (TryParseWebLink(id, out var uri) && WithoutSession(uri) is { } link)
        {
            var cleaned = link.AbsoluteUri;
            return string.Equals(cleaned, pageLink, StringComparison.Ordinal) && cleaned.Length <= ItemIdMaximumLength
                ? cleaned
                : Digest(cleaned);
        }

        // A token — letters, digits and . _ : ~ - — can't be a link of any kind; anything else (a
        // magnet, a relative or protocol-relative link, a query) can carry a credential.
        return PlainTokenPattern().IsMatch(id) && !ContainsSecret(id, secrets) ? id : Digest(id);
    }

    /// <summary>
    /// <paramref name="text"/> with every absolute address cut to its scheme, host and port — no user
    /// info, path, query or fragment — every magnet to <c>magnet:</c>, and the value of every
    /// credential-named pair (<c>passkey=</c>, <c>sid=</c>…) anywhere else masked, for a feed's error
    /// description: a tracker's URL there can carry a passkey, and the app doesn't hold the passkey to
    /// mask it.
    /// </summary>
    /// <remarks>
    /// The pairs are masked wherever they are because where an address ends in free text can't be
    /// known: a .NET exception prints <c>Interstellar%20(2014)</c> as <c>Interstellar (2014)</c>, and the
    /// rest of the URL, query and all, then reads as words after it.
    /// </remarks>
    public static string? WithoutAddresses(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var withoutAddresses = AddressPattern().Replace(text, match => match.Value switch
        {
            var magnet when magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) => "magnet:",
            // An address quoted inside another one, its "://" percent-encoded: decoded to be cut.
            var address when !address.Contains("://", StringComparison.Ordinal) => SchemeAndAuthority(Uri.UnescapeDataString(address)),
            var address => SchemeAndAuthority(address),
        });
        return CredentialPairPattern().Replace(withoutAddresses, match => IsMaskedInText(match.Groups["name"].Value)
            ? match.Groups["name"].Value + match.Groups["equals"].Value + SecretRedaction.Mask
            : match.Value);
    }

    /// <summary>
    /// The names whose values a feed's error text never shows: a credential's, the session's, and the
    /// ones that hold a whole address — a magnet's trackers and sources (<c>tr</c>, <c>ws</c>, <c>xs</c>,
    /// <c>as</c>) and a redirect target (<c>url</c>) — whose path can hold a passkey once a space in the
    /// text has cut the address they belong to short.
    /// </summary>
    private static bool IsMaskedInText(string name) =>
        KeyLikeNames.Contains(name) || TextOnlyMaskedNames.Contains(name);

    /// <summary>
    /// Whether a link's path is a download rather than a page: it ends in <c>.torrent</c>, or a segment
    /// is <c>dl</c> or <c>rss</c> or starts with <c>download</c> — where trackers put a passkey in the
    /// path (<c>/rss/download/123/&lt;passkey&gt;/x.torrent</c>, <c>/torrent/download/123.&lt;key&gt;</c>).
    /// A page link the app wrongly refuses only goes unshown; one it wrongly keeps is a credential shown
    /// to every member.
    /// </summary>
    private static bool LooksLikeDownload(Uri link)
    {
        var path = Uri.UnescapeDataString(link.AbsolutePath);
        return path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)
            || path.Split('/').Any(segment =>
                segment.StartsWith("download", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "dl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "rss", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The link without its fragment and without each query pair whose decoded name is
    /// <c>sid</c> (any letter case), rebuilt from its parts: no trailing <c>?</c> once the query is
    /// empty, so a topic link with and without a session is one link.</summary>
    private static Uri? WithoutSession(Uri uri)
    {
        var kept = Pairs(QueryOf(uri)).Where(pair => !string.Equals(Decode(pair.Name), "sid", StringComparison.OrdinalIgnoreCase)).ToList();
        var link = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        if (kept.Count > 0)
            link += "?" + string.Join('&', kept.Select(pair => pair.Text));

        return Uri.TryCreate(link, UriKind.Absolute, out var rebuilt) ? rebuilt : null;
    }

    /// <summary>
    /// Whether a link names a credential: a key-like parameter, a long value it shares with the
    /// download link (Prowlarr's <c>link=</c> token, the key), or a secret of the call anywhere in it.
    /// </summary>
    private static bool CarriesCredential(Uri link, IReadOnlySet<string> locatorValues, string?[] secrets)
    {
        var pairs = Pairs(QueryOf(link));
        if (pairs.Any(pair => KeyLikeNames.Contains(Decode(pair.Name))))
            return true;
        if (pairs.Any(pair => Forms(pair.Value).Any(value => value.Length >= SharedValueMinimumLength && locatorValues.Contains(value))))
            return true;

        return ContainsSecret(link.AbsoluteUri, secrets) || ContainsSecret(Uri.UnescapeDataString(link.AbsoluteUri), secrets);
    }

    /// <summary>Whether masking would change <paramref name="text"/>: the very rule messages are masked
    /// by, so a value never stored is one a message would never show either.</summary>
    private static bool ContainsSecret(string text, string?[] secrets) =>
        !string.Equals(SecretRedaction.Redact(text, secrets), text, StringComparison.Ordinal);

    private static string Digest(string text) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// <c>scheme://host[:port]</c> of an address as written — read by hand, since a description's text
    /// needn't parse as a <see cref="Uri"/>. The user info goes first, whatever characters it holds
    /// (RFC 3986 allows parentheses in a password); then the host is a bracketed IPv6 address or a run
    /// of letters, digits, dots and hyphens, and the port only digits, so what a quote or a bracket cut
    /// short (<c>user:pa</c> of <c>user:pa'ss@host</c>) shows no more than a name.
    /// </summary>
    private static string SchemeAndAuthority(string address)
    {
        var authorityStart = address.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = address.IndexOfAny(['/', '\\', '?', '#'], authorityStart);
        var authority = address[authorityStart..(authorityEnd < 0 ? address.Length : authorityEnd)];
        authority = authority[(authority.LastIndexOf('@') + 1)..];
        return address[..authorityStart] + HostAndPortPattern().Match(authority).Value;
    }

    /// <summary>
    /// A magnet, or an absolute address (<c>scheme://</c>, or <c>scheme%3A%2F%2F</c> inside another),
    /// up to whitespace, a double quote or an angle bracket. Brackets, parentheses and apostrophes don't
    /// end it: an IPv6 host is bracketed, a file name in a path holds "(2014)", and a password may hold
    /// any of them; cutting too much only leaves less text.
    /// </summary>
    /// <remarks>
    /// The scheme starts only where a run of scheme characters starts (the lookbehind): tried from every
    /// letter of a run, as <c>[a-z][a-z0-9+.\-]*://</c> is, a description of a few megabytes of letters
    /// takes minutes — the feed's text is read whole, before it is cut to length.
    /// </remarks>
    [GeneratedRegex(@"magnet:\?[^\s""<>]*|(?<![a-z0-9+.\-])[a-z0-9+.\-]+(?:://|%3a%2f%2f)[^\s""<>]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    /// <summary>The host and port at the start of an authority: a bracketed IPv6 address, or letters,
    /// digits, dots and hyphens; then <c>:</c> and digits.</summary>
    [GeneratedRegex(@"^(?:\[[0-9a-f:.]*\]|[\p{L}\p{N}.\-]*)(?::[0-9]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostAndPortPattern();

    /// <summary>
    /// A <c>name=value</c> pair in free text, the <c>=</c> perhaps percent-encoded, its value up to a
    /// separator, whitespace, a quote or an angle bracket; <see cref="WithoutAddresses"/> masks the value
    /// when the name is a credential's. The name starts where a run of name characters starts, as in
    /// <see cref="AddressPattern"/>, so a long run of letters is read once.
    /// </summary>
    [GeneratedRegex(@"(?<![a-z0-9_\-%])(?<name>[a-z0-9_\-]+)(?<equals>=|%3d)[^&;\s""'<>]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialPairPattern();

    /// <summary>Letters, digits and <c>. _ : ~ -</c>, 1 to 512 of them: an id that can't be a link of any
    /// kind and fits its column (<c>ProvenancedEntity.SourceItemId</c>).</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._:~\-]{1,512}\z")]
    private static partial Regex PlainTokenPattern();
}
