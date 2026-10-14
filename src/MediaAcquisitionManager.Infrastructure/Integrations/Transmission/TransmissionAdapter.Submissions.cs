using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

// MAM-30 — the download client contract's submission: one torrent-add with the release, the destination's folder, its
// name as the torrent's label and paused false (Vasyl's decision 4), sent on a session id a session-get has just
// confirmed — and once more only after a 409, which Transmission answers before it reads the body (D8).
internal sealed partial class TransmissionAdapter
{
    private const string DuplicateUnreadError = "Transmission already has this torrent, so it added nothing.";

    private const string V2OnlyError =
        "Transmission can't take a torrent that is BitTorrent v2 only, which this release is, so nothing was sent.";

    private const string NotAnAddAnswerError = "The service at this address answered, but not the way Transmission answers a new torrent.";

    /// <summary>The longest label a sentence repeats: a destination's name is at most 64 characters.</summary>
    private const int LabelsMaximumLength = 200;

    /// <summary>A v1 info hash in hexadecimal: Transmission's <c>hashString</c>.</summary>
    private const int V1HashLength = 40;

    /// <summary>A v2 info hash in hexadecimal, which names no Transmission torrent.</summary>
    private const int V2HashLength = 64;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>session-get</c> first (<see cref="OpenAsync"/>), then one <c>torrent-add</c> with exactly <c>metainfo</c> (the
    /// .torrent in base64) or <c>filename</c> (the magnet link), <c>download-dir</c> (the destination's key, exactly as
    /// given), <c>labels</c> (<c>[</c><see cref="DownloadSubmission.Label"/><c>]</c>) and <c>paused</c> <c>false</c>, which
    /// wins over the owner's <c>start-added-torrents</c> — nothing else: no files left out, no priorities, no cookies.
    /// </para>
    /// <para>
    /// <c>torrent-added</c> names the job by its <c>hashString</c>. <c>torrent-duplicate</c> — <c>success</c> too, but
    /// Transmission applied nothing of this add to the torrent it has (<c>tr_torrentNew</c>, ADR 0039) — is
    /// <see cref="IntegrationCallStatus.Refused"/> whatever follows (D9): one <c>torrent-get</c> of that torrent's folder and
    /// labels says where it is, and when that read fails or finds nothing the sentence says less. Any other <c>result</c>
    /// is Transmission's refusal, quoted; it added nothing.
    /// </para>
    /// <para>
    /// A payload Transmission doesn't take, a key that isn't an absolute folder, and a label that is blank or holds a comma
    /// — which Transmission refuses, as it keeps labels apart by commas — are the caller's bugs, thrown as
    /// <see cref="ArgumentException"/> before anything is sent. D12 — a torrent that is BitTorrent v2 only, which
    /// Transmission can't take, is refused without a request; the sender refuses it first, in its own words.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<SubmissionReceipt>> SubmitAsync(
        IntegrationConnectionSettings settings, DownloadSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidatePayload(submission.Payload);
        if (DestinationRules.ValidateClientDestinationKey(submission.DestinationKey) is { } keyError)
            throw new ArgumentException(keyError, nameof(submission));
        if (!NormalizedPath.TryParse(submission.DestinationKey, out _, out _))
            throw new ArgumentException("A folder destination's key must be an absolute path.", nameof(submission));
        if (string.IsNullOrWhiteSpace(submission.Label))
            throw new ArgumentException("A torrent sent to Transmission needs a label: the destination's name.", nameof(submission));
        if (DestinationRules.ValidateLabel(submission.Label) is { } labelError)
            throw new ArgumentException(labelError, nameof(submission));

        if (TorrentInfoHashReader.Read(submission.Payload) is { V1: null, V2: not null })
            return Task.FromResult(IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.Refused, V2OnlyError));

        return CallAsync<SubmissionReceipt>(
            settings, (client, sessionSecrets, token) => SendSubmissionAsync(client, settings, submission, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<SubmissionReceipt>> SendSubmissionAsync(
        HttpClient client, IntegrationConnectionSettings settings, DownloadSubmission submission, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        // A magnet's trackers may carry the owner's passkey: the link, and each address in it as an echo would spell it,
        // are masked like a session in whatever this call says.
        if (submission.Payload.Kind == ReleasePayloadKind.Magnet)
        {
            sessionSecrets.Add(submission.Payload.MagnetUri!);
            foreach (var address in MagnetAddresses(submission.Payload.MagnetUri!))
                sessionSecrets.Add(address);
        }

        var call = NewCall(settings, isTest: false, sessionSecrets);
        var (_, failure) = await OpenAsync(client, call, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<SubmissionReceipt>(failure);

        var (answer, addFailure) = await SendRpcAsync(client, call, TorrentAddMethod, AddArguments(submission), sessionSecrets, cancellationToken);
        if (addFailure is not null)
            return Failed<SubmissionReceipt>(addFailure);

        var secrets = CallSecrets(settings, sessionSecrets);
        if (!answer!.IsSuccess)
        {
            return IntegrationCallResult<SubmissionReceipt>.Failed(
                IntegrationCallStatus.Refused,
                $"Transmission refused the torrent: {RemoteText.Clean(answer.Result, ResultMaximumLength, secrets)}.");
        }

        if (answer.Arguments.TryGetProperty("torrent-added", out var added) && added.ValueKind == JsonValueKind.Object)
            return IntegrationCallResult<SubmissionReceipt>.Succeeded(new SubmissionReceipt(LowerHex(Text(added, "hashString"), V1HashLength)));

        if (answer.Arguments.TryGetProperty("torrent-duplicate", out var duplicate) && duplicate.ValueKind == JsonValueKind.Object)
        {
            var sentence = LowerHex(Text(duplicate, "hashString"), V1HashLength) is { } hash
                ? await DescribeDuplicateAsync(client, call, hash, sessionSecrets, cancellationToken)
                : DuplicateUnreadError;
            return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.Refused, sentence);
        }

        // Transmission may have added it all the same: the caller looks its info hash up rather than reading this as a
        // refusal.
        return IntegrationCallResult<SubmissionReceipt>.Failed(IntegrationCallStatus.UnexpectedResponse, NotAnAddAnswerError);
    }

    /// <summary>
    /// D9 — where the torrent Transmission already has is, and under which labels, from one <c>torrent-get</c> of its
    /// <c>downloadDir</c> and <c>labels</c> — the one read of <c>labels</c> anywhere, only to tell the owner. The short
    /// sentence when that read fails — its transport and timeout too, which would otherwise end the whole call — or doesn't
    /// list the torrent: the add is refused either way.
    /// </summary>
    private async Task<string> DescribeDuplicateAsync(
        HttpClient client, RpcCall call, string hash, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var arguments = new JsonObject { ["ids"] = Names([hash]), ["fields"] = Names(["hashString", "downloadDir", "labels"]) };
        RpcAnswer? answer;
        ConnectionTestResult? failure;
        try
        {
            (answer, failure) = await SendRpcAsync(client, call, TorrentGetMethod, arguments, sessionSecrets, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Unreachable or timed out on the way: still refused. Passed on, the sender would read the timeout as "may have
            // been taken", look the hash up and adopt the torrent Transmission already had — one the app didn't add.
            return DuplicateUnreadError;
        }

        if (failure is not null || !answer!.IsSuccess || TorrentsOf(answer.Arguments) is not [var torrent]
            || LowerHex(Text(torrent, "hashString"), V1HashLength) != hash
            || Text(torrent, "downloadDir") is not { Length: > 0 } downloadDir)
            return DuplicateUnreadError;

        var secrets = CallSecrets(call.Settings, sessionSecrets);
        var labels = torrent.TryGetProperty("labels", out var listed) && listed.ValueKind == JsonValueKind.Array
            ? listed.EnumerateArray().Where(label => label.ValueKind == JsonValueKind.String).Select(label => label.GetString()!).ToList()
            : [];
        var labelled = labels.Count == 0
            ? "with no label"
            : $"with the label “{RemoteText.Clean(string.Join(", ", labels), LabelsMaximumLength, secrets)}”";
        return $"Transmission already has this torrent, in “{RemoteText.Clean(downloadDir, FolderMaximumLength, secrets)}” {labelled}, so it added nothing.";
    }

    /// <summary>
    /// Throws when <paramref name="payload"/> isn't a whole release Transmission takes: a .torrent file with its bytes, or
    /// a magnet link — <c>magnet:?</c> and no control character. The prefix matters: Transmission reads <c>filename</c> as
    /// an address to fetch or a path on its own disk before a magnet link, and the app sends it neither.
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
                throw new ArgumentException($"Transmission takes a .torrent file or a magnet link, not {payload.Kind}.", "submission");
        }
    }

    /// <summary>
    /// The addresses a magnet link names — its trackers (<c>tr</c>) and sources (<c>ws</c>, <c>xs</c>, <c>as</c>),
    /// numbered or not — percent-decoded, as a service that echoed one would spell it. Masking the whole link alone would
    /// miss a tracker's address echoed on its own, passkey and all.
    /// </summary>
    private static IEnumerable<string> MagnetAddresses(string magnet) =>
        magnet["magnet:?".Length..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Split('.')[0].ToLowerInvariant() is "tr" or "ws" or "xs" or "as")
            .Select(pair => Uri.UnescapeDataString(pair[1]));

    /// <summary>The <c>torrent-add</c> arguments: the release, the folder, the label and <c>paused</c> — and nothing else
    /// (<see cref="SubmitAsync"/>).</summary>
    private static JsonObject AddArguments(DownloadSubmission submission)
    {
        var arguments = new JsonObject();
        if (submission.Payload.Kind == ReleasePayloadKind.TorrentFile)
            arguments["metainfo"] = Convert.ToBase64String(submission.Payload.TorrentFile!);
        else
            arguments["filename"] = submission.Payload.MagnetUri;

        // Never trimmed or rewritten: the folder exactly as the destination stores it, which Transmission keeps as sent
        // and lists back as the torrent's downloadDir.
        arguments["download-dir"] = submission.DestinationKey;
        arguments["labels"] = Names([submission.Label!]);
        arguments["paused"] = false;
        return arguments;
    }

    /// <summary><paramref name="value"/> lower-cased when it is <paramref name="length"/> ASCII hexadecimal characters —
    /// <see cref="char.IsAsciiHexDigit"/> takes no other script's digits — else <c>null</c>.</summary>
    private static string? LowerHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
}
