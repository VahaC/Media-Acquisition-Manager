using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

/// <summary>
/// MAM-30 — Transmission's legacy RPC behind the core's download client contract (Adapter, ADR 0005, ADR 0039), in place
/// of qBittorrent: the connection test and the session every call opens here, the RPC itself in
/// <c>TransmissionAdapter.Rpc.cs</c> and <see cref="TransmissionSessions"/>, the folders it confirms in
/// <c>TransmissionAdapter.Destinations.cs</c>, the one write in <c>TransmissionAdapter.Submissions.cs</c>, the lookups in
/// <c>TransmissionAdapter.Jobs.cs</c>, a torrent's files in <c>TransmissionAdapter.Files.cs</c>, and every torrent with
/// its files in <c>TransmissionAdapter.Holdings.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Four RPC methods and no other: <c>session-get</c>, <c>free-space</c> and <c>torrent-get</c> read, and one write,
/// <c>torrent-add</c>, sent once on a session id a <c>session-get</c> has just confirmed and sent again only after a 409,
/// which Transmission answers before it reads the body (ADR 0013). Nothing that stops, removes, moves, relocates or
/// renames a torrent or its files (<c>torrent-stop</c>, <c>torrent-remove</c>, <c>torrent-set-location</c>,
/// <c>torrent-rename-path</c>, <c>torrent-set</c>, <c>session-set</c> — ADR 0014, ADR 0031): the convention test of the
/// method constants below keeps it so. Every answer is read as an allowlist, property by property by name (house rule 8),
/// and a torrent's trackers and magnet link, which can carry the owner's passkey, are never asked for.
/// </para>
/// <para>
/// Transmission 4.0 or newer only (Vasyl's decision 2): <c>labels</c> in <c>torrent-add</c>, which carries a
/// destination's name, came with 4.0's RPC version 17.
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — every sentence is a key of <c>transmission.json</c> (or, where qBittorrent's words are the same
/// but for the name, of <c>integrations.json</c> through <see cref="IntegrationAdapterMessages"/>) and its values:
/// Transmission's own words — a <c>result</c>, a folder, its labels, its version — are vendor values, cleaned and masked
/// where they are read (<see cref="VendorText.Clean"/>).
/// </para>
/// </remarks>
internal sealed partial class TransmissionAdapter(IHttpClientFactory httpClients)
    : HttpIntegrationAdapter(httpClients, HttpClientName), IDownloadClient
{
    /// <summary>The stored type id — part of the on-disk contract (ADR 0005).</summary>
    public const string TypeId = "transmission";

    /// <summary>The named client this adapter's requests go through, so a test can swap its handler.</summary>
    public const string HttpClientName = TypeId;

    /// <summary>MAM-47 — the service's name, which the descriptor shows and the sentences shared with the other adapters
    /// name it by (<see cref="IntegrationAdapterMessages"/>), static readers included.</summary>
    private const string DisplayName = "Transmission";

    /// <summary>The RPC method that reads Transmission's version and settings, which every call starts with.</summary>
    internal const string SessionGetMethod = "session-get";

    /// <summary>The RPC method that tells whether Transmission can see a folder (ADR 0039).</summary>
    internal const string FreeSpaceMethod = "free-space";

    /// <summary>The RPC method that lists torrents and their files.</summary>
    internal const string TorrentGetMethod = "torrent-get";

    /// <summary>The one write: the RPC method that adds a torrent.</summary>
    internal const string TorrentAddMethod = "torrent-add";

    /// <summary>Longer than any version Transmission reports (<c>4.1.3</c>) once its build is cut off.</summary>
    private const int VersionMaximumLength = 32;

    /// <summary>
    /// <c>rpc-version</c> 17 is Transmission 4.0.0 (its RPC specification's history; 3.00 sent 16), the first whose
    /// <c>torrent-add</c> takes <c>labels</c>.
    /// </summary>
    private const int MinimumRpcVersion = 17;

    /// <summary><c>rpc-version</c> 18 is Transmission 4.1.0, which keeps a name as the operating system takes it (D20).</summary>
    private const int CleansNamesByOperatingSystemRpcVersion = 18;

    /// <summary>The folder names' longest a sentence repeats: Linux's <c>PATH_MAX</c>.</summary>
    private const int FolderMaximumLength = 4096;

    /// <summary>The longest text of Transmission's a sentence quotes, such as a <c>result</c>: its sentences are short.</summary>
    private const int ResultMaximumLength = 300;

    private static readonly Message NotTransmissionError = Message.Of("transmission.not-transmission");

    /// <summary><c>rpc-version-semver</c> 5.3.0 is Transmission 4.0.0; 4.0 also sends it first.</summary>
    private static readonly Version MinimumSemver = new(5, 3, 0);

    /// <summary><c>rpc-version-semver</c> 6.0.0 is Transmission 4.1.0.</summary>
    private static readonly Version CleansNamesByOperatingSystemSemver = new(6, 0, 0);

    /// <summary>
    /// What every call asks <c>session-get</c> for, and nothing more: never its whole answer, which holds the session id
    /// and settings the app has no business with.
    /// </summary>
    private static readonly string[] SessionFields =
        ["version", "rpc-version", "rpc-version-semver", "download-dir", "incomplete-dir", "incomplete-dir-enabled"];

    /// <inheritdoc/>
    public override IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        DisplayName,
        IntegrationKind.DownloadClient,
        // Optional: Transmission asks for no password unless rpc-authentication-required is on; without one it takes any
        // credentials or none (MAM-29).
        UserName: CredentialRequirement.Optional,
        Password: CredentialRequirement.Optional,
        ApiKey: CredentialRequirement.NotUsed,
        ReportsFilePaths: true);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>torrent-add</c> takes a .torrent (<c>metainfo</c>) and a magnet link (<c>filename</c>) alike, and answers with
    /// the torrent's <c>hashString</c>, which names it for good. Its destinations are folders the app proposes and it
    /// confirms (ADR 0039). It writes a torrent's own layout only, which no setting changes, an unfinished file as
    /// <c>.part</c> when <c>rename-partial-files</c> is on — the default from 4.1, and the linuxserver image's on 4.0 too, so
    /// always looked at. It takes no torrent that is BitTorrent v2 only ("unrecognized info"), and never renames a job: the
    /// v1 hash it is named by stays.
    /// </remarks>
    public DownloadClientCapabilities Capabilities { get; } = new(
        PayloadKinds: [ReleasePayloadKind.TorrentFile, ReleasePayloadKind.Magnet],
        DestinationsHavePaths: true,
        SubmissionReturnsJobId: true,
        DestinationKind: DownloadDestinationKind.Folder,
        FilePlacement: TorrentFilePlacement.OwnLayoutOnly,
        PartialFileSuffix: ".part",
        TakesV2OnlyTorrents: false,
        JobIdsMayChange: false);

    /// <summary>
    /// The session every call opens (<see cref="OpenAsync"/>): <c>session-get</c>, the 409 handshake on the way, the
    /// version gate and the temporary folder. Never asks whether these credentials were refused before: trying them again
    /// is what the owner asked for (D7).
    /// </summary>
    protected override async Task<ConnectionTestResult> TestAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var call = NewCall(settings, isTest: true, sessionSecrets);
        var (session, failure) = await OpenAsync(client, call, sessionSecrets, cancellationToken);
        return failure ?? ConnectionTestResult.Succeeded(IntegrationAdapterMessages.Connected(DisplayName, session!.Version));
    }

    /// <summary>
    /// How every call starts, the test included: the refusal of these settings remembered from an earlier call answered
    /// without a request (not for the test, D7); the session id kept for them sent, when there is one; then one
    /// <c>session-get</c> of <see cref="SessionFields"/> through <see cref="SendRpcAsync"/>, read by
    /// <see cref="ReadSession"/>.
    /// </summary>
    /// <remarks>
    /// The read is also what the one write stands on: it renews a session id Transmission has rotated (hourly, and on a
    /// restart) and refuses a version or setting the app can't work with, so <c>torrent-add</c> goes out on an id
    /// Transmission has just accepted and needs a second try only when the id rotated in between (D8).
    /// </remarks>
    private async Task<(SessionFacts? Session, ConnectionTestResult? Failure)> OpenAsync(
        HttpClient client, RpcCall call, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var settings = call.Settings;
        if (!call.IsTest)
        {
            if (_sessions.FindRefusal(settings) is { } refused)
                return (null, RememberedRefusal(refused, settings));
            if (_sessions.Find(settings) is { } kept)
            {
                call.Session = kept;
                sessionSecrets.Add(kept.Id);
            }
        }

        var (answer, failure) = await SendRpcAsync(
            client, call, SessionGetMethod, new JsonObject { ["fields"] = Names(SessionFields) }, sessionSecrets, cancellationToken);
        if (failure is not null)
            return (null, failure);

        // Past Transmission's own checks with these credentials: what a test is for (D7).
        if (call.IsTest)
            _sessions.ForgetRefusal(settings);
        if (!answer!.IsSuccess)
            return (null, RequestRefused(answer, settings, sessionSecrets));
        return ReadSession(answer.Arguments, CallSecrets(settings, sessionSecrets));
    }

    /// <summary>
    /// <c>session-get</c>'s answer: the version, short and cleaned, which must be 4.0 or newer; then the temporary folder,
    /// which must be off (D6); and how this Transmission cleans names (D20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The version is judged by the RPC's own numbers, not by the text: <c>rpc-version-semver</c> 5.3.0 or later when it is
    /// sent (4.0 sends it first), else <c>rpc-version</c> 17 or later. An answer without the version, the download folder
    /// or the temporary folder's switch is not Transmission's.
    /// </para>
    /// <para>
    /// D6 — with the temporary folder on, Transmission keeps an unfinished download in <c>incomplete-dir</c> and moves it
    /// to its <c>downloadDir</c> once complete, and no torrent field says where it is meanwhile: the overwrite check, the
    /// holdings and the new file's check would look in the wrong folder. Every call is refused until it is off, with how.
    /// </para>
    /// </remarks>
    private static (SessionFacts? Session, ConnectionTestResult? Failure) ReadSession(JsonElement arguments, string?[] secrets)
    {
        if (Text(arguments, "version") is not { } reported || ShortVersion(reported, secrets) is not { Text.Length: > 0 } version)
            return (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTransmissionError));

        switch (IsAtLeast(arguments, MinimumSemver, MinimumRpcVersion))
        {
            case null:
                return (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTransmissionError));
            case false:
                return (null, ConnectionTestResult.Failed(
                    IntegrationCallStatus.UnsupportedVersion, Message.Of("transmission.too-old", ("version", version))));
        }

        if (Text(arguments, "download-dir") is not { } downloadDir || Flag(arguments, "incomplete-dir-enabled") is not { } temporaryFolderOn)
            return (null, ConnectionTestResult.Failed(IntegrationCallStatus.UnexpectedResponse, NotTransmissionError));
        if (temporaryFolderOn)
        {
            var temporaryFolder = VendorText.Clean(Text(arguments, "incomplete-dir"), FolderMaximumLength, secrets);
            return (null, ConnectionTestResult.Failed(
                IntegrationCallStatus.Refused, Message.Of("transmission.temporary-folder", ("folder", temporaryFolder))));
        }

        var cleansByOperatingSystem = IsAtLeast(arguments, CleansNamesByOperatingSystemSemver, CleansNamesByOperatingSystemRpcVersion) == true;
        return (new SessionFacts(version, downloadDir, NameReadingOf(cleansByOperatingSystem, downloadDir)), null);
    }

    /// <summary>
    /// D20 — how this Transmission turns a torrent's name into the folder or file it writes: 4.0 cleans it the portable
    /// way on every system (<see cref="TorrentNameReadings.Portable"/>); 4.1 cleans it for the system it runs on, which its
    /// download folder's style tells — Windows (<see cref="TorrentNameReadings.WindowsSafe"/>) or Linux
    /// (<see cref="TorrentNameReadings.Verbatim"/>). <c>null</c> when 4.1's folder is no path to tell the system by: the
    /// content's place is then unknown rather than guessed.
    /// </summary>
    private static Func<string, IReadOnlyList<string>>? NameReadingOf(bool cleansByOperatingSystem, string downloadDir)
    {
        if (!cleansByOperatingSystem)
            return TorrentNameReadings.Portable;
        if (!NormalizedPath.TryParse(downloadDir, out var folder, out _))
            return null;
        return folder.Style == PathStyle.Windows ? TorrentNameReadings.WindowsSafe : TorrentNameReadings.Verbatim;
    }

    /// <summary>
    /// Whether the RPC is at least <paramref name="semver"/> by <c>rpc-version-semver</c>, when it sends one this app
    /// reads, else at least <paramref name="rpcVersion"/> by <c>rpc-version</c>; <c>null</c> when it sends neither.
    /// </summary>
    private static bool? IsAtLeast(JsonElement arguments, Version semver, int rpcVersion)
    {
        if (Text(arguments, "rpc-version-semver") is { } text && SemverPattern().Match(text) is { Success: true } match
            && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(match.Groups[2].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            && int.TryParse(match.Groups[3].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return new Version(major, minor, patch) >= semver;

        return Whole(arguments, "rpc-version") is { } version ? version >= rpcVersion : null;
    }

    /// <summary>
    /// The version as a sentence shows it: <c>version</c>'s first word — <c>4.1.3</c> of <c>4.1.3 (838877323f)</c>, its
    /// build left out — cleaned of anything a service shouldn't put into a sentence.
    /// </summary>
    private static VendorText ShortVersion(string reported, string?[] secrets)
    {
        var trimmed = reported.Trim();
        var end = trimmed.IndexOfAny([' ', '\t', '(']);
        return VendorText.Clean(end < 0 ? trimmed : trimmed[..end], VersionMaximumLength, secrets);
    }

    /// <summary><c>5.3.0</c>, or <c>6.0.1</c>: three ASCII numbers, anything after them ignored.</summary>
    [GeneratedRegex(@"^([0-9]{1,9})\.([0-9]{1,9})\.([0-9]{1,9})")]
    private static partial Regex SemverPattern();

    /// <summary>The names as a JSON list, for a <c>fields</c> argument.</summary>
    private static JsonArray Names(IEnumerable<string> names) => new([.. names.Select(name => (JsonNode)JsonValue.Create(name))]);

    /// <summary>
    /// What <c>session-get</c> told a call: the version as sentences show it, the default download folder exactly as sent,
    /// and how this Transmission cleans a torrent's name (D20; <c>null</c> when it can't be told).
    /// </summary>
    private sealed record SessionFacts(VendorText Version, string DownloadDir, Func<string, IReadOnlyList<string>>? NameReading);
}
