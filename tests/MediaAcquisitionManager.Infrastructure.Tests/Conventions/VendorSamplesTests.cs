using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Infrastructure.Tests.Conventions;

// The vendor samples are real answers from the reference installation (MAM-6), so the adapters are
// tested against what the services actually send. A real answer can carry the credentials that
// fetched it: Prowlarr's API key in every download link, a tracker passkey in a magnet or a tracker
// URL, the Toloka forum session in a topic link; MAM-33 — TMDb's API Read Access Token in a recorded
// Authorization header; MAM-29 — Transmission's Basic credential and its CSRF session id, Jackett's
// download token, a cookie, the bytes of a downloaded .torrent. A sample refreshed by hand could bring
// one back into the repository, so each of those shapes fails the build here.
public sealed partial class VendorSamplesTests
{
    private const string ReadmeFileName = "README.md";

    // The parameter names the collector redacted (collect-api-samples.mjs, MAM-6).
    private const string KeyLikeNames =
        "apikey|api_key|api_token|apitoken|jackett_apikey|passkey|pk|uk|tp|token|access_token|link|authkey|torrent_pass|rsskey|key|secret|password|passwd|pwd|pass|auth|sig|signature|x-plex-token";

    private static readonly string[] Integrations = ["Jellyfin", "QBittorrent", "Transmission", "Torznab", "Ffprobe", "Tmdb"];

    public static TheoryData<string> Samples() => new(SampleFiles());

    [Fact]
    public void Samples_cover_every_integration_and_sit_in_its_folder()
    {
        //Arrange
        var samples = SampleFiles();

        //Act
        var folders = samples.Select(sample => sample.Split('/')[0]).ToHashSet(StringComparer.Ordinal);

        //Assert
        Assert.All(Integrations, integration => Assert.Contains(integration, folders));
        // A sample outside the integration folders would be scanned but documented nowhere.
        Assert.Subset(Integrations.ToHashSet(StringComparer.Ordinal), folders);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Sample_parses_as_its_format(string sample)
    {
        //Arrange
        var text = await File.ReadAllTextAsync(PathOf(sample), TestContext.Current.CancellationToken);

        //Act
        var exception = Record.Exception(() => Parse(sample, text));

        //Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Sample_carries_no_credential(string sample)
    {
        //Arrange
        var text = await File.ReadAllTextAsync(PathOf(sample), TestContext.Current.CancellationToken);

        //Act
        var leaks = LeaksIn(text);

        //Assert
        // On failure: sanitize the value the way VendorSamples/README.md describes.
        Assert.Empty(leaks);
    }

    // The rules themselves are checked, so a broken pattern can't let every sample pass.
    [Theory]
    [InlineData("<link>http://192.168.1.195:9696/1/download?apikey=0123456789abcdef0123456789abcdef&amp;link=REDACTED</link>", "key-like URL parameter")]
    [InlineData("<enclosure url=\"http://192.168.1.195:9696/1/download?link=Q2ZESjhQcm90ZWN0ZWQ&amp;apikey=REDACTED\" />", "key-like URL parameter")]
    [InlineData("<link>http://jackett:9117/dl/toloka/?jackett_apikey=0123456789abcdef&amp;path=x</link>", "key-like URL parameter")]
    [InlineData("\"url\": \"http://host/dl?id=1\\u0026passkey=9f8e7d6c\"", "key-like URL parameter")]
    [InlineData("?next=%2Fdl.php%3Fid%3D1%26passkey%3D9f8e7d6c", "key-like parameter inside an encoded URL")]
    [InlineData("<guid>https://toloka.to/t699400?sid=4f2a9c0e1b7d</guid>", "session id")]
    [InlineData("Cookie: QBT_SID_8080=Zk3m9Qp2Xy", "session id")]
    [InlineData("\"magnet_uri\": \"magnet:?xt=urn:btih:1ec4&dn=Taken&tr=http%3A%2F%2Fbt.toloka.to%2Fannounce\"", "magnet tracker list")]
    [InlineData("value=\"magnet:?xt=urn:btih:1ec4&amp;dn=Пам'ять&amp;tr=udp://tracker\"", "magnet tracker list")]
    [InlineData("\"magnet_uri\": \"magnet:?tr=http://bt.toloka.to/9f8e7d6c&xt=urn:btih:1ec4\"", "magnet tracker list")]
    [InlineData("\"tracker\": \"http://bt.toloka.to/announce.php?passkey=REDACTED\"", "tracker announce URL")]
    [InlineData("\"tracker\": \"http://bt.hurtom.com/9f8e7d6c/ann\"", "tracker announce URL")]
    [InlineData("\"tracker\": \"http://bt.hurtom.com/9f8e7d6c5b4a39281706f5e4d3c2b1a0\"", "tracker path")]
    [InlineData("\"Path\": \"smb://vasyl:hunter2@nas/movies/Film.mkv\"", "user info in a URL")]
    [InlineData("\"Path\": \"smb://REDACTED@hunter2@nas/movies/Film.mkv\"", "user info in a URL")]
    [InlineData("?next=http%3A%2F%2Fbt.toloka.to%2Fdl.php%3Fid%3D1", "URL-encoded URL")]
    [InlineData("\"AccessToken\": \"0123456789abcdef0123456789abcdef\"", "credential field")]
    [InlineData("\"apiKey\": \"0123456789abcdef0123456789abcdef\"", "credential field")]
    [InlineData("\"proxy_password\": \"hunter2secret\"", "credential field")]
    [InlineData("\"session-id\": \"4Yk0eLz9pQ2mN7sT1uV3wX5yZ8aB6cD0eF2gH4iJ6kL8mN0o\"", "credential field")]
    [InlineData("Authorization: MediaBrowser Client=\"MAM\", Token=\"0123456789abcdef\"", "credential header")]
    [InlineData("X-Api-Key: 0123456789abcdef", "credential header")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiIwMTIzIn0.c2lnbmF0dXJl", "bearer token")]
    [InlineData("authorization:bearer 0123456789abcdef", "bearer token")]
    [InlineData("\"Authorization\": \"Bearer eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiIwMTIzIn0.c2lnbmF0dXJl\"", "bearer token")]
    [InlineData("Authorization: Basic dmFoYWM6aHVudGVyMg==", "Basic credentials")]
    [InlineData("\"Authorization\": \"Basic dmFoYWM6aHVudGVyMg==\"", "Basic credentials")]
    [InlineData("X-Transmission-Session-Id: 4Yk0eLz9pQ2mN7sT1uV3wX5yZ8aB6cD0eF2gH4iJ6kL8mN0o", "Transmission session id")]
    [InlineData("<p><code>X-Transmission-Session-Id: 4Yk0eLz9pQ2mN7sT1uV3wX5yZ8aB6cD0eF2gH4iJ6kL8mN0o</code></p>", "Transmission session id")]
    [InlineData("<link>http://192.168.1.195:9117/dl/toloka/?jackett_apikey=REDACTED&amp;path=Q2ZESjhQcm90ZWN0ZWQ&amp;file=Interstellar</link>", "Jackett download token")]
    [InlineData("Set-Cookie: Jackett=CfDJ8Protected; path=/; httponly", "cookie")]
    [InlineData("Cookie: Jackett=CfDJ8Protected", "cookie")]
    [InlineData("d8:announce5:x/abc13:creation datei1791903931e", "the content of a .torrent")]
    [InlineData("d10:created by13:Transmission4:infod6:lengthi140e4:name6:poster", "the content of a .torrent")]
    public void Leak_rules_flag_a_planted_credential(string text, string rule)
    {
        //Arrange

        //Act
        var leaks = LeaksIn(text);

        //Assert
        Assert.Contains(rule, leaks);
    }

    [Theory]
    [InlineData("<link>http://192.168.1.195:9696/1/download?apikey=REDACTED&amp;link=REDACTED&amp;file=Interstellar+(2014)+Ukr%2FEng</link>")]
    [InlineData("<link>http://prowlarr/1/download?file=Ann+(2019)&amp;apikey=REDACTED</link>")]
    [InlineData("<guid>https://toloka.to/t699400?sid=REDACTED</guid>")]
    [InlineData("<comments>https://toloka.to/t685514</comments>")]
    [InlineData("\"magnet_uri\": \"magnet:?xt=urn:btih:1ec4&dn=Taken.%20Trilogy&xl=81850937909\"")]
    [InlineData("\"tracker\": \"http://bt.toloka.to/REDACTED\"")]
    [InlineData("\"reannounce\": 41")]
    [InlineData("\"HasPassword\": false, \"TokenLifetime\": 3600")]
    [InlineData("\"title\": \"Брати по зброї / Brothers in Arms\"")]
    [InlineData("Authorization: Bearer REDACTED")]
    [InlineData("\"overview\": \"A bearer of bad news\"")]
    [InlineData("Authorization: Basic REDACTED")]
    [InlineData("WWW-Authenticate: Basic realm=\"Transmission\"")]
    [InlineData("X-Transmission-Session-Id: REDACTED")]
    [InlineData("<p><code>X-Transmission-Session-Id: REDACTED</code></p>")]
    [InlineData("Access-Control-Expose-Headers: X-Transmission-Session-Id\nDate: Tue, 13 Oct 2026 15:14:54 GMT")]
    [InlineData("<li> When reading a response, get its X-Transmission-Session-Id header and remember it")]
    [InlineData("<link>http://192.168.1.195:9117/dl/toloka/?jackett_apikey=REDACTED&amp;path=REDACTED&amp;file=Interstellar+(2014)</link>")]
    [InlineData("\"name\": \"Big Buck Bunny/Big Buck Bunny.en.srt\", \"downloadDir\": \"/scratch\"")]
    [InlineData("Set-Cookie: REDACTED")]
    [InlineData("\"session-id\": \"REDACTED\"")]
    public void Leak_rules_let_redacted_and_ordinary_text_through(string text)
    {
        //Arrange

        //Act
        var leaks = LeaksIn(text);

        //Assert
        Assert.Empty(leaks);
    }

    private static List<string> SampleFiles() =>
        Directory.EnumerateFiles(RepositoryPaths.VendorSamples, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepositoryPaths.VendorSamples, file).Replace('\\', '/'))
            .Where(sample => !string.Equals(sample, ReadmeFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string PathOf(string sample) => Path.Combine(RepositoryPaths.VendorSamples, sample);

    private static void Parse(string sample, string text)
    {
        switch (Path.GetExtension(sample))
        {
            case ".json":
                using (JsonDocument.Parse(text)) { }
                break;
            case ".xml":
                XDocument.Parse(text);
                break;
            case ".txt":
                if (string.IsNullOrWhiteSpace(text))
                    throw new FormatException("The sample is empty.");
                break;
            default:
                throw new FormatException($"Samples are .json, .xml or .txt; {sample} is none of them.");
        }
    }

    private static List<string> LeaksIn(string text)
    {
        // XML escapes the & between URL parameters and JSON encoders may too; the rules read the
        // URLs as they are.
        var plain = text
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("\\u0026", "&", StringComparison.OrdinalIgnoreCase);
        return Rules()
            .Where(rule => rule.Pattern.IsMatch(plain))
            .Select(rule => rule.Name)
            .ToList();
    }

    private static (string Name, Regex Pattern)[] Rules() =>
    [
        ("key-like URL parameter", KeyLikeParameter()),
        ("key-like parameter inside an encoded URL", EncodedKeyLikeParameter()),
        ("session id", SessionId()),
        ("magnet tracker list", MagnetTrackerList()),
        ("tracker announce URL", AnnounceUrl()),
        ("tracker path", TrackerPath()),
        ("user info in a URL", UserInfo()),
        ("URL-encoded URL", EncodedUrl()),
        ("credential field", CredentialField()),
        ("credential header", CredentialHeader()),
        ("bearer token", BearerToken()),
        ("Basic credentials", BasicCredentials()),
        ("Transmission session id", TransmissionSessionId()),
        ("Jackett download token", JackettDownloadToken()),
        ("cookie", Cookie()),
        ("the content of a .torrent", TorrentContent()),
    ];

    [GeneratedRegex(@"[?&;](?:" + KeyLikeNames + @")(?:=|%3D)(?!REDACTED)[^&""'<>\s]", RegexOptions.IgnoreCase)]
    private static partial Regex KeyLikeParameter();

    [GeneratedRegex(@"(?:%3F|%26)(?:" + KeyLikeNames + @")%3D(?!REDACTED)", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedKeyLikeParameter();

    [GeneratedRegex(@"\b(?:QBT_)?SID(?:_\d+)?=(?!REDACTED)[^&""'<>\s;]", RegexOptions.IgnoreCase)]
    private static partial Regex SessionId();

    // Tracker parameters are removed from a magnet, not redacted, wherever they stand in it.
    [GeneratedRegex(@"magnet:\?(?:[^""<>]*&)?(?:tr|ws|xs|as)(?:\.\d+)?=", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetTrackerList();

    // Only the path is looked at, so a release named "Ann" in a query string is not a tracker.
    [GeneratedRegex(@"://[^/\s""'<>]+/[^\s""'<>?#&]*\b(?:announce|ann)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AnnounceUrl();

    // qBittorrent's tracker field keeps the scheme and the host; the path can hold the passkey.
    [GeneratedRegex(@"""tracker""\s*:\s*""[a-z]+://[^/""]+/(?!REDACTED"")[^""]", RegexOptions.IgnoreCase)]
    private static partial Regex TrackerPath();

    // Everything before the last @ of the authority is user info; only REDACTED may stand there.
    [GeneratedRegex(@"://(?!REDACTED@[^/\s""<>@]*(?:[/\s""<>]|$))[^/\s""<>]*@")]
    private static partial Regex UserInfo();

    [GeneratedRegex(@"(?:https?|udp|wss?)%3A%2F%2F", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedUrl();

    // A string value under a credential-like JSON key: Jellyfin's AccessToken, Prowlarr's apiKey,
    // qBittorrent's proxy and mail passwords; MAM-29 — Transmission's session-id, which a session-get
    // without a fields list returns.
    [GeneratedRegex(@"""[A-Za-z_-]*(?:api_?key|access_?token|password|passwd|passkey|secret|cookie|X-Emby-Token|session-id)""\s*:\s*""(?!REDACTED""|"")", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialField();

    [GeneratedRegex(@"\b(?:X-Api-Key|X-Emby-Token)\s*:\s*(?!REDACTED)\S|\bMediaBrowser\b[^\r\n]*\bToken=""(?!REDACTED"")", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialHeader();

    // MAM-33 — TMDb's v4 token travels as Authorization: Bearer; recorded as a header line or as JSON, only
    // REDACTED may follow the scheme.
    [GeneratedRegex(@"\bAuthorization[""']?\s*:\s*[""']?Bearer\s+(?!REDACTED\b)[^\s""']", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // MAM-29 — Transmission takes its user name and password as HTTP Basic: the token is both, base64-encoded.
    [GeneratedRegex(@"\bAuthorization[""']?\s*:\s*[""']?Basic\s+(?!REDACTED\b)[^\s""']", RegexOptions.IgnoreCase)]
    private static partial Regex BasicCredentials();

    // MAM-29 — Transmission's CSRF session id, in its header and in the <code> line of the 409 page that quotes it.
    [GeneratedRegex(@"\bX-Transmission-Session-Id\s*:\s*(?!REDACTED)[^\s<""']", RegexOptions.IgnoreCase)]
    private static partial Regex TransmissionSessionId();

    // MAM-29 — Jackett's download link carries a protected token in path=; with the key it opens the download,
    // as Prowlarr's link= does.
    [GeneratedRegex(@"[?&;]path=(?!REDACTED)[^&""'<>\s]", RegexOptions.IgnoreCase)]
    private static partial Regex JackettDownloadToken();

    [GeneratedRegex(@"\b(?:Set-)?Cookie\s*:\s*(?!REDACTED)\S", RegexOptions.IgnoreCase)]
    private static partial Regex Cookie();

    // MAM-29 — a downloaded .torrent's announce URLs carry the tracker passkey; a sample keeps its headers only.
    [GeneratedRegex(@"d8:announce|4:infod", RegexOptions.IgnoreCase)]
    private static partial Regex TorrentContent();
}
