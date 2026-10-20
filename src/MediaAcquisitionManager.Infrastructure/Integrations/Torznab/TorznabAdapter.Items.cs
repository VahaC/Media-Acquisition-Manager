using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Torznab;

// MAM-10 — the releases of a t=search, t=movie or t=tvsearch (MAM-32) answer, every field read by name (house rule 8) and
// every number and address parsed, never trusted: an item this app can't use is skipped, never the
// search. MAM-33 — the newest releases (t=search with no q) are read alike.
internal sealed partial class TorznabAdapter
{
    /// <summary>What a release's title column holds (<c>ReleaseEntity.Title</c>).</summary>
    private const int TitleMaximumLength = 1000;

    /// <summary>
    /// The longest locator kept, in UTF-8 bytes: sealed, it is <c>v1:</c> and the base64 of 12 + n + 16
    /// bytes, which for 6000 is 8043 characters — inside <c>ReleaseEntity.PayloadLocatorEncrypted</c>'s
    /// 8192. The download links seen are 300 to 2000 characters long.
    /// </summary>
    private const int LocatorMaximumBytes = 6000;

    /// <summary>The longest origin a sentence repeats: an address the owner would type is far shorter.</summary>
    private const int OriginMaximumLength = 100;

    /// <summary>
    /// The digits an IMDb title id has at least: <c>tt0816692</c>. Newznab's <c>imdb</c> attribute is the
    /// number, which Jackett writes without its leading zeros (<c>816692</c>), and an id read so must
    /// equal the one a library holds.
    /// </summary>
    private const int ImdbIdMinimumDigits = 7;

    /// <summary>
    /// The listed releases in the feed's order, at most the query's limit, those in none of the asked
    /// categories left out. An item this app can't use is skipped, never the search: one malformed
    /// listing must not hide the rest.
    /// </summary>
    private static IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>> ReadHits(
        XElement channel, IntegrationConnectionSettings settings, ReleaseSearchQuery query, string?[] secrets)
    {
        var feed = new Uri(EndpointFor(settings.BaseUrl));
        var kindAsked = KindAsked(query);
        var hits = new List<ReleaseSearchHit>();
        foreach (var item in channel.Elements("item"))
        {
            if (hits.Count == query.Limit)
                break;
            if (ReadHit(item, feed, settings.ApiKey, kindAsked, secrets) is not { } read)
                continue;

            // MAM-10 — the payload fetch only ever requests the feed's own origin, so a link elsewhere
            // could never be fetched. Prowlarr and Jackett build their links from the request's Host
            // header, a reverse proxy's or a base-URL override: the whole answer is then wrong, and the
            // owner learns why now rather than at the first download.
            if (read.WebLink is { } link && !TorznabLinks.SameOrigin(link, feed))
            {
                var origin = VendorText.Clean(TorznabLinks.Origin(link), OriginMaximumLength, secrets);
                return IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>.Failed(
                    IntegrationCallStatus.UnexpectedResponse, Message.Of("torznab.search.links-elsewhere", ("origin", origin)));
            }

            if (IsInCategories(read.Hit.CategoryIds, query.CategoryIds))
                hits.Add(read.Hit);
        }

        return IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>.Succeeded(hits);
    }

    /// <summary>
    /// One listed release, or <c>null</c> when it has no title or no download link this app takes;
    /// with its parsed web link, when the locator is one, for the origin rule.
    /// </summary>
    /// <param name="kindAsked">What the search that listed it asked for (<see cref="KindAsked"/>): the kind of
    /// a release listed in no category at all (MAM-32).</param>
    private static (ReleaseSearchHit Hit, Uri? WebLink)? ReadHit(XElement item, Uri feed, string? apiKey, ReleaseKind kindAsked, string?[] secrets)
    {
        var title = RemoteText.Clean((string?)item.Element("title"), TitleMaximumLength, secrets);
        if (title.Length == 0)
            return null;

        var attributes = item.Elements().Where(element => element.Name.LocalName is "attr").ToList();
        var locator = FirstNonBlank(
            (string?)item.Element("enclosure")?.Attribute("url"),
            (string?)item.Element("link"),
            AttributeValues(attributes, "magneturl").FirstOrDefault());
        if (locator is null)
            return null;

        Uri? webLink = null;
        string stored;
        if (TorznabLinks.IsMagnet(locator))
        {
            stored = locator;
        }
        else if (TorznabLinks.TryParseWebLink(locator, out var link))
        {
            webLink = link;
            // MAM-10 — a stored release never holds a copy of the gateway's key (ADR 0007 keeps it
            // sealed to its integration alone), and a rotated key keeps old results working: the
            // fetch puts the connection's current key back.
            stored = TorznabLinks.WithoutApiKey(locator, apiKey);
        }
        else
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(stored) > LocatorMaximumBytes)
            return null;

        var locatorValues = TorznabLinks.QueryValues(locator);
        var pageLink = TorznabLinks.PageLink((string?)item.Element("comments"), feed, locatorValues, secrets);
        var categoryIds = CategoryIds(item, attributes);
        var description = Description((string?)item.Element("description"), title, secrets);
        var kind = KindOf(categoryIds, kindAsked, title, description);
        var hit = new ReleaseSearchHit(
            TorznabLinks.ItemId((string?)item.Element("guid"), pageLink, secrets),
            title,
            FirstSize((string?)item.Element("size"), (string?)item.Element("enclosure")?.Attribute("length"), AttributeValues(attributes, "size").FirstOrDefault()),
            Count(AttributeValues(attributes, "seeders").FirstOrDefault()),
            Count(AttributeValues(attributes, "peers").FirstOrDefault()),
            Published((string?)item.Element("pubDate")),
            pageLink,
            categoryIds,
            // Prowlarr and Jackett label every torrent enclosure application/x-bittorrent, even one
            // whose link turns into a magnet: only a magnet locator says what the payload is, and
            // otherwise the fetch decides.
            webLink is null ? ReleasePayloadKind.Magnet : null,
            InfoHash(AttributeValues(attributes, "infohash").FirstOrDefault()),
            Identifiers(attributes, kind),
            new ReleasePayloadLocator(stored),
            kind,
            description);
        return (hit, webLink);
    }

    /// <summary>
    /// MAM-33 — the item's <c>&lt;description&gt;</c>, cleaned and cut like the title; <c>null</c> when it is
    /// blank or the title itself. Toloka puts its topic's own title there, Ukrainian title first
    /// ("Ліхтарі (Сезон 1) / Lanterns (Season 1) (2026) …"), where Prowlarr's title keeps the English part
    /// alone, and for a Ukrainian-only show nothing readable at all ("(2026) WEB-DL 1080p (S1E1-25 of ?)";
    /// VendorSamples).
    /// </summary>
    private static string? Description(string? value, string title, string?[] secrets)
    {
        var description = RemoteText.Clean(value, TitleMaximumLength, secrets);
        return description.Length == 0 || string.Equals(description, title, StringComparison.Ordinal) ? null : description;
    }

    /// <summary>The values of the item's <c>attr</c> elements named <paramref name="name"/>, in any
    /// namespace (<c>torznab:</c> or <c>newznab:</c>) and any letter case.</summary>
    private static IEnumerable<string?> AttributeValues(IEnumerable<XElement> attributes, string name) =>
        attributes
            .Where(attribute => string.Equals(((string?)attribute.Attribute("name"))?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Select(attribute => (string?)attribute.Attribute("value"));

    private static string? FirstNonBlank(params string?[] values) =>
        values.Select(value => value?.Trim()).FirstOrDefault(value => !string.IsNullOrEmpty(value));

    /// <summary>The first of the size the item states (<c>&lt;size&gt;</c>, the enclosure's length, the
    /// <c>size</c> attribute) that is a whole number of bytes.</summary>
    private static long? FirstSize(params string?[] values) =>
        values
            .Select(value => long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) ? size : (long?)null)
            .FirstOrDefault(size => size is not null);

    /// <summary>A count of zero or more: digits only, no sign or separators.</summary>
    private static int? Count(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : null;

    private static int? PositiveCount(string? value) => Count(value) is > 0 and var count ? count : null;

    /// <summary>A v1 (40) or v2 (64) info hash in hex, lower-cased like the client reports it.</summary>
    private static string? InfoHash(string? value) =>
        value?.Trim() is { } hash && InfoHashPattern().IsMatch(hash) ? hash.ToLowerInvariant() : null;

    /// <summary>
    /// The registry ids the listing gives (MAM-12): IMDb from <c>imdbid</c> and <c>imdb</c>, TMDb from
    /// <c>tmdbid</c>, TVDB from <c>tvdbid</c> (MAM-32), at most one per scheme and IMDb first. A listing
    /// that gives two different values of one scheme gives none of it: a listing that contradicts itself
    /// can't be matched to anything (the <c>LibraryWorkGrouping</c> rule for an item naming two values of
    /// one scheme). Toloka lists none.
    /// </summary>
    /// <remarks>
    /// MAM-32 — a TV listing's <c>tmdbid</c> is a show's, in TMDb's other numbering (ADR 0017), so it is
    /// read as <see cref="MediaIdentifierScheme.TmdbTv"/> and can never match a movie with that number.
    /// </remarks>
    private static List<MediaIdentifier> Identifiers(IReadOnlyList<XElement> attributes, ReleaseKind kind)
    {
        var identifiers = new List<MediaIdentifier>(3);
        var imdbValues = AttributeValues(attributes, "imdbid").Concat(AttributeValues(attributes, "imdb"));
        if (OnlyValue(imdbValues.Select(ImdbId)) is { } imdb)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Imdb, imdb));
        if (OnlyValue(AttributeValues(attributes, "tmdbid").Select(NumericId)) is { } tmdb)
            identifiers.Add(new MediaIdentifier(kind == ReleaseKind.Tv ? MediaIdentifierScheme.TmdbTv : MediaIdentifierScheme.Tmdb, tmdb));
        if (OnlyValue(AttributeValues(attributes, "tvdbid").Select(NumericId)) is { } tvdb)
            identifiers.Add(new MediaIdentifier(MediaIdentifierScheme.Tvdb, tvdb));
        return identifiers;
    }

    /// <summary>
    /// MAM-32 — a release is TV when it lists a TV category (Torznab's 5000 to 5999: TV, TV/HD, TV/Anime,
    /// TV/Documentary …, where Prowlarr files Toloka's «Телесеріали» and «Мультсеріали»), and a movie
    /// otherwise; one that lists no category is what the search that found it asked for.
    /// </summary>
    /// <remarks>
    /// MAM-33 — except TV/Anime and TV/Documentary (<see cref="HoldsFilmsAndSeries"/>), where Toloka files
    /// films beside series: a release listing no other TV category is TV only when its title or
    /// description names seasons or episodes (<see cref="ReleaseWorkClaims.ClaimsSeasonsOrEpisodes"/>), and a
    /// film otherwise — "Akira (1988)" a film, "Death Note (E1-31 of 37) (2006)" a series (Vasyl's decision 6,
    /// ADR 0020). The category still decides everything else.
    /// </remarks>
    private static ReleaseKind KindOf(IReadOnlyList<string> categoryIds, ReleaseKind kindAsked, string title, string? description)
    {
        if (categoryIds.Any(id => HoldsTv(id) && !HoldsFilmsAndSeries(id)))
            return ReleaseKind.Tv;
        if (categoryIds.Any(HoldsFilmsAndSeries))
        {
            return ReleaseWorkClaims.ClaimsSeasonsOrEpisodes(title)
                || description is not null && ReleaseWorkClaims.ClaimsSeasonsOrEpisodes(description)
                ? ReleaseKind.Tv
                : ReleaseKind.Movie;
        }

        return categoryIds.Count == 0 ? kindAsked : ReleaseKind.Movie;
    }

    /// <summary>
    /// MAM-32 — what a search asked for, which a listing in no category is taken to be: a TV search TV, a
    /// movie search movies, and a general search TV when every category it asked holds TV — the title tier
    /// <c>ReleaseSearchPlanning.ForTv</c> sends a source whose TV search takes no text — and movies
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// The asked ids are tested with <see cref="HoldsTv"/>, the rule that marked them <c>HoldsTv</c> in the
    /// caps the planning picked them from, so the query alone tells it. Any other id asked beside them, a
    /// gateway's own section included (Toloka's 100xxx), may hold a movie, and the search is then not TV.
    /// MAM-33 — TV/Anime and TV/Documentary still count as TV here: the query asked for TV, and nothing in a
    /// listing in no category says otherwise; the marker rule is only for a listing that lists them
    /// (<see cref="KindOf"/>). The newest releases are asked as a general search, and read alike.
    /// </remarks>
    private static ReleaseKind KindAsked(ReleaseSearchQuery query) =>
        query.Mode switch
        {
            ReleaseSearchMode.Tv => ReleaseKind.Tv,
            ReleaseSearchMode.General or ReleaseSearchMode.Latest
                when query.CategoryIds.Count > 0 && query.CategoryIds.All(HoldsTv) => ReleaseKind.Tv,
            _ => ReleaseKind.Movie,
        };

    /// <summary>The one id among <paramref name="values"/> (<c>null</c> ones are none); <c>null</c> when
    /// there is none or more than one.</summary>
    private static string? OnlyValue(IEnumerable<string?> values)
    {
        var distinct = values.OfType<string>().Distinct(StringComparer.Ordinal).Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>
    /// An IMDb title id from <c>tt0816692</c> or its digits alone, with or without leading zeros, as
    /// <c>tt</c> and at least <see cref="ImdbIdMinimumDigits"/> digits — <c>tt</c> lower-cased as IMDb
    /// writes it. <c>null</c> for anything else: blank, all zeros (Jackett writes <c>0</c> for none), or a
    /// value <see cref="MediaIdentifierRules"/> refuses.
    /// </summary>
    private static string? ImdbId(string? value)
    {
        var text = value?.Trim() ?? "";
        var digits = text.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit) || digits.All(digit => digit == '0'))
            return null;
        var id = "tt" + digits.PadLeft(ImdbIdMinimumDigits, '0');
        return MediaIdentifierRules.IsImdbId(id) ? id : null;
    }

    /// <summary>A TMDb or TVDB id, digits only; <c>null</c> for anything else, blank and all zeros
    /// included (Jackett writes <c>0</c> for none).</summary>
    private static string? NumericId(string? value)
    {
        var text = value?.Trim() ?? "";
        return MediaIdentifierRules.IsTmdbId(text) && text.Any(digit => digit != '0') ? text : null;
    }

    /// <summary>
    /// An RSS <c>pubDate</c> ("Mon, 03 Aug 2026 00:00:00 +0300") in UTC; <c>null</c> when it can't be
    /// read. The weekday is dropped first: it says nothing the date doesn't, and a wrong one would fail
    /// the whole date.
    /// </summary>
    private static DateTime? Published(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var date = WeekdayPrefixPattern().Match(value) is { Success: true } weekday ? value[weekday.Length..] : value;
        return DateTimeOffset.TryParse(
            date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var published)
            ? published.UtcDateTime
            : null;
    }

    /// <summary>The <c>&lt;category&gt;</c> texts, then the <c>category</c> attributes: well-formed ids
    /// only, each once, in the order listed.</summary>
    private static List<string> CategoryIds(XElement item, IEnumerable<XElement> attributes) =>
        item.Elements("category").Select(category => (string?)category.Value)
            .Concat(AttributeValues(attributes, "category"))
            .Select(id => id?.Trim())
            .OfType<string>()
            .Where(id => CategoryIdPattern().IsMatch(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Whether a hit stays for the asked categories: one it lists is asked for, or lies in the thousand
    /// of an asked parent (<c>x000</c>; 2045 when 2000 was asked) — both gateways expand a parent so.
    /// A hit that lists none stays: the feed filtered it, or nothing can be said.
    /// </summary>
    private static bool IsInCategories(IReadOnlyList<string> listed, IReadOnlyList<string> asked)
    {
        if (asked.Count == 0 || listed.Count == 0)
            return true;

        return listed.Any(id => asked.Any(parent => string.Equals(id, parent, StringComparison.Ordinal) || IsUnder(id, parent)));
    }

    private static bool IsUnder(string id, string parent) =>
        int.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var parentNumber)
        && parentNumber >= 1000
        && parentNumber % 1000 == 0
        && int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        && number / 1000 == parentNumber / 1000;

    [GeneratedRegex(@"^(?:[0-9a-f]{40}|[0-9a-f]{64})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InfoHashPattern();

    /// <summary>"Mon," or "Monday," before an RFC 822 date, ASCII letters only.</summary>
    [GeneratedRegex(@"^\s*[A-Za-z]+\s*,")]
    private static partial Regex WeekdayPrefixPattern();
}
