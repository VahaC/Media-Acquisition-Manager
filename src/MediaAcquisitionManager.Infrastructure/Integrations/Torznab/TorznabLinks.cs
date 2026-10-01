using System.Diagnostics.CodeAnalysis;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

/// <summary>
/// The rules every link a Torznab feed lists is held to (MAM-10): which download links the app fetches
/// and from where, and how a release keeps its download link without a copy of the feed's key; which
/// page links and ids are safe to store in clear and show is in <c>TorznabLinks.Public.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// A feed's answer is the owner's gateway relaying a tracker: its download links carry the gateway's
/// API key (ADR 0004), its page links the owner's forum session (Toloka's <c>sid</c>), and a gateway
/// that has no id for a release uses the tracker's own download link, passkey and all, as the id
/// (Prowlarr does). The id, the page link and the title are stored in clear and printed by the hit's
/// <c>ToString</c>, so each is cleaned here or refused.
/// </para>
/// <para>
/// Every check works on the parsed <see cref="Uri"/>, never on a pattern over the text: the request is
/// made to what the parser understood, so that is what is judged.
/// </para>
/// </remarks>
internal static partial class TorznabLinks
{
    /// <summary>
    /// What a stored download link holds in place of the feed's key: <c>{api-key}</c>, percent-encoded so
    /// the link stays one a parser takes, and no key a gateway generates (Prowlarr's and Jackett's are
    /// 32 hexadecimal characters).
    /// </summary>
    public const string ApiKeyPlaceholder = "%7Bapi-key%7D";

    /// <summary>
    /// A query value this long or longer that a page link shares with the download link is the download
    /// link's token, not a coincidence — the shortest value <see cref="SecretRedaction"/> masks.
    /// </summary>
    private const int SharedValueMinimumLength = 8;

    private const string MagnetPrefix = "magnet:?";

    private static readonly char[] PairSeparators = ['&', ';'];

    /// <summary>
    /// Whether two addresses are one origin: the same scheme, host and port. The host is compared in
    /// its ASCII (punycode) form, in any letter case and without one trailing dot, and a default port
    /// equals the explicit one; an address with user info is never the same origin, since what the
    /// owner entered has none.
    /// </summary>
    public static bool SameOrigin(Uri first, Uri second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (!first.IsAbsoluteUri || !second.IsAbsoluteUri || first.UserInfo.Length > 0 || second.UserInfo.Length > 0)
            return false;

        var host = HostOf(first);
        return host.Length > 0
            && string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(host, HostOf(second), StringComparison.OrdinalIgnoreCase)
            && first.Port == second.Port;
    }

    /// <summary><c>scheme://host[:port]</c> of an address: what a sentence may name of a link, never its
    /// user info, path or query.</summary>
    public static string Origin(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

    /// <summary>
    /// <paramref name="text"/> as an absolute http or https address without user info or control
    /// characters — the only download links the app fetches, and the only page links it keeps.
    /// </summary>
    public static bool TryParseWebLink(string? text, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsControl) || !Uri.TryCreate(text, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme is not ("http" or "https") || parsed.UserInfo.Length > 0 || HostOf(parsed).Length == 0)
            return false;

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a magnet link the app hands to a client: <c>magnet:?</c> in any
    /// letter case, an exact topic that is a BitTorrent info hash (<c>xt=urn:btih:</c>, or v2's
    /// <c>urn:btmh:</c>), and no control characters.
    /// </summary>
    public static bool IsMagnet(string text) =>
        text.StartsWith(MagnetPrefix, StringComparison.OrdinalIgnoreCase)
        && !text.Any(char.IsControl)
        && Pairs(text[MagnetPrefix.Length..]).Any(pair => IsExactTopic(Decode(pair.Name)) && IsInfoHashUrn(Decode(pair.Value)));

    /// <summary>
    /// <paramref name="link"/> with every query value that is <paramref name="apiKey"/> — as sent,
    /// percent-decoded, or with <c>+</c> for a space — replaced by <see cref="ApiKeyPlaceholder"/>, and
    /// every other character as it was.
    /// </summary>
    public static string WithoutApiKey(string link, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return link;

        return ReplaceQueryValues(
            link,
            value => Forms(value).Any(form => string.Equals(form, apiKey, StringComparison.Ordinal)) ? ApiKeyPlaceholder : null);
    }

    /// <summary>
    /// <paramref name="link"/> with <see cref="ApiKeyPlaceholder"/> replaced by the escaped
    /// <paramref name="apiKey"/>; <c>null</c> when the link needs a key and there is none.
    /// </summary>
    public static string? WithApiKey(string link, string? apiKey)
    {
        var needsKey = false;
        var withKey = ReplaceQueryValues(link, value =>
        {
            if (!string.Equals(value, ApiKeyPlaceholder, StringComparison.Ordinal))
                return null;
            needsKey = true;
            return string.IsNullOrEmpty(apiKey) ? value : Uri.EscapeDataString(apiKey);
        });

        return needsKey && string.IsNullOrEmpty(apiKey) ? null : withKey;
    }

    /// <summary>Every query value of <paramref name="link"/> as sent, percent-decoded and with <c>+</c>
    /// for a space: what a page link or an id must not share with it.</summary>
    public static IReadOnlySet<string> QueryValues(string link) =>
        Pairs(QueryOf(link)).SelectMany(pair => Forms(pair.Value)).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// What a feed's error about the download of <paramref name="link"/> may echo: the link itself and
    /// each query value (Prowlarr's <c>link=</c> token is base64 with <c>+/=</c>, percent-encoded in the
    /// link), as sent and decoded. Values under 8 characters are masked nowhere
    /// (<see cref="SecretRedaction"/>), so they are left out.
    /// </summary>
    public static IEnumerable<string> SecretsOf(Uri uri, string link)
    {
        string[] links = [link, uri.AbsoluteUri];
        var queryValues = links.SelectMany(text => Pairs(QueryOf(text))).SelectMany(pair => Forms(pair.Value));
        return links.Concat(links.Select(Uri.UnescapeDataString))
            .Concat(queryValues)
            .Where(value => value.Length >= SharedValueMinimumLength)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// The path segments of a download link long enough to mask, as sent and decoded: a gateway may put
    /// a token in the path. Kept apart from <see cref="SecretsOf"/> because a path is mostly words —
    /// Prowlarr's is <c>/1/download</c> — and masking the app's own sentences with them would turn
    /// "answered the download" into "answered the ***"; they mask the feed's own text only.
    /// </summary>
    public static IEnumerable<string> PathSegmentsOf(Uri uri) =>
        uri.AbsolutePath.Split('/')
            .SelectMany(segment => new[] { segment, Uri.UnescapeDataString(segment) })
            .Where(value => value.Length >= SharedValueMinimumLength)
            .Distinct(StringComparer.Ordinal);

    /// <summary>The query of a parsed address, without its <c>?</c>.</summary>
    private static string QueryOf(Uri uri) => uri.Query.Length > 1 ? uri.Query[1..] : "";

    /// <summary>The query of a link as written: from the first <c>?</c> to the fragment, if the
    /// <c>?</c> comes before any <c>#</c>.</summary>
    private static string QueryOf(string link)
    {
        var (start, end) = QueryBounds(link);
        return start < 0 ? "" : link[start..end];
    }

    private static (int Start, int End) QueryBounds(string link)
    {
        var question = link.IndexOf('?', StringComparison.Ordinal);
        var hash = link.IndexOf('#', StringComparison.Ordinal);
        if (question < 0 || (hash >= 0 && hash < question))
            return (-1, -1);
        return (question + 1, hash >= 0 ? hash : link.Length);
    }

    /// <summary>
    /// The query's pairs as written, split on <c>&amp;</c> and <c>;</c> (both separate pairs to the
    /// servers these links come from), empty ones dropped.
    /// </summary>
    private static List<(string Text, string Name, string Value)> Pairs(string query) =>
        query.Split(PairSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.IndexOf('=', StringComparison.Ordinal) is var equals and >= 0
                ? (Text: pair, Name: pair[..equals], Value: pair[(equals + 1)..])
                : (Text: pair, Name: pair, Value: ""))
            .ToList();

    /// <summary>
    /// <paramref name="link"/> with the value of each query pair <paramref name="replacement"/> answers
    /// for replaced, and every other character — separators, names, the fragment — as it was.
    /// </summary>
    private static string ReplaceQueryValues(string link, Func<string, string?> replacement)
    {
        var (start, end) = QueryBounds(link);
        if (start < 0)
            return link;

        var builder = new StringBuilder(link.Length).Append(link, 0, start);
        var position = start;
        while (position <= end)
        {
            var separator = link.IndexOfAny(PairSeparators, position, end - position);
            var pairEnd = separator < 0 ? end : separator;
            var pair = link[position..pairEnd];
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0 && replacement(pair[(equals + 1)..]) is { } value)
                builder.Append(pair, 0, equals + 1).Append(value);
            else
                builder.Append(pair);

            if (separator < 0)
                break;
            builder.Append(link[separator]);
            position = separator + 1;
        }

        return builder.Append(link, end, link.Length - end).ToString();
    }

    /// <summary>A query value as sent, percent-decoded, and decoded with <c>+</c> for a space — the
    /// three ways a server may read it.</summary>
    private static string[] Forms(string value) =>
        [value, Uri.UnescapeDataString(value), Uri.UnescapeDataString(value.Replace('+', ' '))];

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    /// <summary><c>xt</c>, or a numbered one (<c>xt.1</c>) of a magnet with several.</summary>
    private static bool IsExactTopic(string name) =>
        string.Equals(name, "xt", StringComparison.OrdinalIgnoreCase) || name.StartsWith("xt.", StringComparison.OrdinalIgnoreCase);

    private static bool IsInfoHashUrn(string value) =>
        value.Length > "urn:btih:".Length
        && (value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The host in its ASCII form without one trailing dot — <c>feed.test.</c> and <c>feed.test</c> are one
    /// host — or empty when it has none: <see cref="Uri.IdnHost"/> throws for some hosts
    /// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> accepts (one holding a zero-width joiner), and
    /// a listing must be skipped for it, never fail the search.
    /// </summary>
    private static string HostOf(Uri uri)
    {
        string host;
        try
        {
            host = uri.IdnHost;
        }
        catch (UriFormatException)
        {
            return "";
        }

        return host.EndsWith('.') ? host[..^1] : host;
    }
}
