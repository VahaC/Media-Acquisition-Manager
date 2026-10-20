using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

// MAM-13 — the download client contract's submission: one POST api/v2/torrents/add with the release, the
// destination's category and automatic torrent management, sent once on a session a read has just confirmed.
// Its answer is told apart by status and body shape, which covers 4.4.1–5.1 (text) and 5.2 (JSON) alike
// without asking which version answered.
internal sealed partial class QBittorrentAdapter
{
    private static readonly Message AddFailedError = Message.Of("qbittorrent.add.failed");

    private static readonly Message AddedNothingError = Message.Of("qbittorrent.add.added-nothing");

    private static readonly Message UnreadableTorrentError = Message.Of("qbittorrent.add.unreadable-torrent");

    private static readonly Message NotAnAddAnswerError = IntegrationAdapterMessages.NotAnAddAnswer(DisplayName);

    /// <summary>
    /// The name the .torrent is uploaded under. qBittorrent names a torrent by its info dictionary, never by
    /// the uploaded file's name, so a fixed one loses nothing and tells it nothing about the owner's search.
    /// </summary>
    private const string TorrentUploadName = "release.torrent";

    /// <summary>A qBittorrent id (TorrentID): the v1 info hash, or the v2 one cut to its first 40 characters.</summary>
    private const int TorrentIdLength = 40;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The session and <c>GET /api/v2/app/version</c> first (<see cref="OpenCheckedSessionAsync"/>): the read
    /// renews a kept session qBittorrent has forgotten and refuses a version the app doesn't work with, so the
    /// one <c>POST /api/v2/torrents/add</c> goes out on a session qBittorrent has just accepted. Whatever
    /// answers it, a 403 included, is the outcome: nothing is sent again (house rule 8).
    /// </para>
    /// <para>
    /// <c>multipart/form-data</c>, the way qBittorrent's Web UI sends it: the release as a <c>torrents</c>
    /// file part or a <c>urls</c> text part, the destination's key as <c>category</c> exactly as stored, and
    /// <c>autoTMM=true</c> (MAM-13's decision 2) — under manual management, qBittorrent's default, a category
    /// alone decides no folder. Never <c>savepath</c>, <c>downloadPath</c>, <c>useDownloadPath</c>,
    /// <c>rename</c>, <c>tags</c>, <c>paused</c> or <c>stopped</c>: a destination is never overridden by a
    /// path (specification §3.2), and the rest are the owner's qBittorrent defaults to decide.
    /// </para>
    /// <para>
    /// A payload qBittorrent doesn't take, or one missing its .torrent file or magnet link, is the caller's
    /// bug, thrown as <see cref="ArgumentException"/> before anything is sent; so is a key no destination can
    /// hold (<see cref="DestinationRules.ValidateClientDestinationKey(string)"/>), which qBittorrent would read as no
    /// category and save to its default folder.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<SubmissionReceipt>> SubmitAsync(
        IntegrationConnectionSettings settings, DownloadSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidatePayload(submission.Payload);
        if (DestinationRules.ValidateClientDestinationKey(submission.DestinationKey) is { } keyError)
            // MAM-47 — a developer's text naming the broken rule's key: Infrastructure renders no sentence, and this fires
            // only on a caller's bug (the destination passed the same rules when it was saved).
            throw new ArgumentException($"The destination's key breaks the rule {keyError.Key}.", nameof(submission));

        return CallAsync<SubmissionReceipt>(
            settings, (client, sessionSecrets, token) => SendSubmissionAsync(client, settings, submission, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<SubmissionReceipt>> SendSubmissionAsync(
        HttpClient client, IntegrationConnectionSettings settings, DownloadSubmission submission, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        // A magnet's trackers may carry the owner's passkey: the link, and each address in it as an echo would
        // spell it, are masked like a session in whatever this call says.
        if (submission.Payload.Kind == ReleasePayloadKind.Magnet)
        {
            sessionSecrets.Add(submission.Payload.MagnetUri!);
            foreach (var address in MagnetAddresses(submission.Payload.MagnetUri!))
                sessionSecrets.Add(address);
        }

        var (call, _, _, failure) = await OpenCheckedSessionAsync(client, settings, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<SubmissionReceipt>(failure);

        using var response = await SendPostAsync(client, settings, call!.Session, "api/v2/torrents/add", AddForm(submission), cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                return ReadAddAnswer(await ReadTextAsync(response, cancellationToken));
            // 5.2: taken, but still pending, so it names no job yet; the caller finds it by its info hash.
            case HttpStatusCode.Accepted:
                return IntegrationCallResult<SubmissionReceipt>.Succeeded(new SubmissionReceipt(null));
            // 5.2: nothing was added — a torrent it already has, or a link it couldn't use.
            case HttpStatusCode.Conflict:
                return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.Refused, AddedNothingError);
            // APIErrorType::BadData: the .torrent didn't load, so nothing was added.
            case HttpStatusCode.UnsupportedMediaType:
                return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.UnexpectedResponse, UnreadableTorrentError);
            case HttpStatusCode.Forbidden:
                if (call.Session is not { } refused)
                    return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.AuthenticationFailed, SignInNeededError);
                // Never kept once refused, but never renewed for this write either: the next call signs in.
                _sessions.Drop(settings, refused);
                return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.UnexpectedResponse, SessionRefusedError);
            case HttpStatusCode.NotFound:
                return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.UnexpectedResponse, NotFoundError);
            default:
                return DescribeUnexpectedStatus<SubmissionReceipt>(response, settings, sessionSecrets);
        }
    }

    /// <summary>
    /// Throws when <paramref name="payload"/> isn't a whole release qBittorrent takes: a .torrent file with
    /// its bytes, or a magnet link — <c>magnet:?</c> and no control character, since qBittorrent reads
    /// <c>urls</c> one link per line, and a line break would hand it a second one.
    /// </summary>
    private static void ValidatePayload(ReleasePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        switch (payload.Kind)
        {
            case ReleasePayloadKind.TorrentFile when payload.TorrentFile is not { Length: > 0 }:
                throw new ArgumentException("The release's payload carries no .torrent file.", "submission");
            case ReleasePayloadKind.Magnet when payload.MagnetUri is not { } magnet
                || !magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)
                || magnet.Any(char.IsControl):
                throw new ArgumentException("The release's payload carries no usable magnet link.", "submission");
            case ReleasePayloadKind.TorrentFile or ReleasePayloadKind.Magnet:
                return;
            default:
                throw new ArgumentException($"qBittorrent takes a .torrent file or a magnet link, not {payload.Kind}.", "submission");
        }
    }

    /// <summary>
    /// The addresses a magnet link names — its trackers (<c>tr</c>) and sources (<c>ws</c>, <c>xs</c>,
    /// <c>as</c>), numbered or not — percent-decoded, as a service that echoed one would spell it. Masking the
    /// whole link alone would miss a tracker's address echoed on its own, passkey and all.
    /// </summary>
    private static IEnumerable<string> MagnetAddresses(string magnet) =>
        magnet["magnet:?".Length..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Split('.')[0].ToLowerInvariant() is "tr" or "ws" or "xs" or "as")
            .Select(pair => Uri.UnescapeDataString(pair[1]));

    /// <summary>The <c>torrents/add</c> form: the release, the category and automatic torrent management —
    /// and nothing else (<see cref="SubmitAsync"/>).</summary>
    private static MultipartFormDataContent AddForm(DownloadSubmission submission)
    {
        var form = new MultipartFormDataContent();
        if (submission.Payload.Kind == ReleasePayloadKind.TorrentFile)
        {
            var torrent = new ByteArrayContent(submission.Payload.TorrentFile!);
            torrent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
            form.Add(torrent, "torrents", TorrentUploadName);
        }
        else
        {
            form.Add(new StringContent(submission.Payload.MagnetUri!), "urls");
        }

        // Never trimmed: the key is qBittorrent's own name for the category, surrounding spaces included, and
        // a name it doesn't have would be created in its default folder.
        form.Add(new StringContent(submission.DestinationKey), "category");
        form.Add(new StringContent("true"), "autoTMM");
        return form;
    }

    /// <summary>
    /// A 200 to <c>torrents/add</c>: 4.4.1 to 5.1 answer the text <c>Ok.</c> when anything was added and
    /// <c>Fails.</c> otherwise; 5.2 answers a JSON object, whose <c>added_torrent_ids</c> name the job when
    /// they name exactly one (<see cref="AddedTorrentId"/>). Anything else is not qBittorrent's answer.
    /// </summary>
    private static IntegrationCallResult<SubmissionReceipt> ReadAddAnswer(string body)
    {
        var text = body.Trim();
        if (string.Equals(text, "Ok.", StringComparison.Ordinal))
            return IntegrationCallResult<SubmissionReceipt>.Succeeded(new SubmissionReceipt(null));
        if (string.Equals(text, "Fails.", StringComparison.Ordinal))
            return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.Refused, AddFailedError);

        JsonDocument? document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            document = null;
        }

        using (document)
        {
            return document?.RootElement.ValueKind == JsonValueKind.Object
                ? IntegrationCallResult<SubmissionReceipt>.Succeeded(new SubmissionReceipt(AddedTorrentId(document.RootElement)))
                : IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAnAddAnswerError);
        }
    }

    /// <summary>
    /// The one id 5.2 lists in <c>added_torrent_ids</c>, lower-case — or <c>null</c> when it lists none,
    /// several, or one that isn't a TorrentID. The release was added all the same (5.2 answers 409 when
    /// nothing was), so <c>null</c> leaves the caller to find the job by its info hash rather than failing.
    /// </summary>
    private static string? AddedTorrentId(JsonElement answer) =>
        answer.TryGetProperty("added_torrent_ids", out var ids)
        && ids.ValueKind == JsonValueKind.Array
        && ids.GetArrayLength() == 1
        && ids[0].ValueKind == JsonValueKind.String
        && ids[0].GetString() is { Length: TorrentIdLength } id
        && id.All(char.IsAsciiHexDigit)
            ? id.ToLowerInvariant()
            : null;
}
