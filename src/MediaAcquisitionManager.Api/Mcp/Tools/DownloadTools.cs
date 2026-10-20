using System.ComponentModel;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>get_download_options</c> and <c>send_download</c>: the web app's Download dialog, its choices
/// and its Send.</summary>
/// <remarks>
/// The dialog's own rules on top of <see cref="IDownloadService"/>, which the browser keeps today (Vasyl's decision d): the
/// pick it opens on (<see cref="DownloadDialogPreselection"/>), the overwrite check of the destination picked, and Send
/// held for a folder its client can't see — a new send's, as a retry is answered with its job. A send is always Keep both: <see cref="SendDownloadRequest"/> has no
/// replacement field, and nothing here schedules one (ADR 0040). Built per call by the SDK, with the request's services.
/// MAM-47 (ADR 0042) — every sentence it writes, its summaries and the service's refusals alike, in the language of the
/// MCP request (<see cref="IRequestTexts"/>): English unless the client sends <c>Accept-Language</c>.
/// </remarks>
[McpServerToolType]
public sealed class DownloadTools(IDownloadService downloads, IRequestTexts texts, McpToolResults results)
{
    [McpServerTool(
        Name = "get_download_options",
        Title = "Download options",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DownloadOptionsToolResult))]
    [Description(
        "For one release, the download clients and destinations MAM can send it to — the choice the web app's Download "
        + "dialog offers — with the client and destination the dialog would preselect, and the overwrite check of that "
        + "destination (or of destinationId when you give one): whether MAM can check for files the release would write "
        + "over there, and if it can't, why. sendHeld is true when that destination can't take a send at all, with the "
        + "reason to tell the user. Call it before send_download, show the user the choice, and take the ids from this "
        + "answer only.")]
    public async Task<CallToolResult> GetDownloadOptionsAsync(
        ClaimsPrincipal user,
        [Description("The release's id: a release id from search_releases or list_search_releases.")]
        Guid releaseId,
        [Description("A destination of this answer to check instead of the preselected one, for when the user wants "
            + "another.")]
        Guid? destinationId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        var listed = await downloads.GetOptionsAsync(releaseId.ToString(), userId, cancellationToken);
        if (listed.Options is not { } options)
            return McpToolResults.Error(texts.Render(listed.Error!));

        var (client, destination) = DownloadDialogPreselection.Of(options);
        var checkedDestination = destination;
        if (destinationId is { } asked)
        {
            // Only one the dialog offers, as only those can be picked there: the check of a destination whose client is
            // switched off, or doesn't take this release, would read as if the release could go there.
            checkedDestination = options.Clients
                .SelectMany(option => option.Destinations)
                .FirstOrDefault(offered => offered.Id == asked);
            if (checkedDestination is null)
                return McpToolResults.Error(texts.Render(DownloadToolErrors.DestinationNotOffered));
        }

        // As the dialog asks once a destination is picked: one live question to its client, and none without a pick.
        DownloadOverwriteCheckResponse? check = null;
        if (checkedDestination is not null)
        {
            var answered = await downloads.GetOverwriteCheckAsync(checkedDestination.Id.ToString(), userId, cancellationToken);
            if (answered.Answer is not { } answer)
                return McpToolResults.Error(texts.Render(answered.Error!));
            check = answer;
        }

        var held = check?.NotCheckedReason == DownloadOverwriteCheck.NotCheckedFolderNotSeen;
        return results.Success(
            texts.Render(OptionsSummary(options, client, destination, checkedDestination, check)),
            new DownloadOptionsToolResult(
                options, client?.Id, destination?.Id, checkedDestination?.Id, check, held, held ? texts.Render(McpToolErrors.SendHeldFolderNotSeen) : null));
    }

    [McpServerTool(
        Name = "send_download",
        Title = "Send a download",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DownloadJobDto))]
    [PersonalAccessTokenWrite]
    // Claude Code asks the user before every send, even with mam's tools allowed "always": a download takes disk space
    // and bandwidth, and the model must never be the one who decides it.
    [McpMeta("anthropic/requiresUserInteraction", true)]
    [Description(
        "Sends a release to a download client, into one of its destinations, and returns the download job MAM follows "
        + "from then on. Call it only after the user has confirmed this exact release, client and destination; take the "
        + "ids from search_releases or list_search_releases and get_download_options, never guess them. The copies "
        + "already in the library are kept: a send never replaces one. A release already being downloaded is refused. "
        + "Give a requestId of your own and the same one again when you retry after an error or a timeout, so the "
        + "release is sent once; a job that comes back Failed says why in its error, and sending again needs a new "
        + "requestId. Needs an access token with Read and download access.")]
    public async Task<CallToolResult> SendDownloadAsync(
        ClaimsPrincipal user,
        [Description("The release's id, from search_releases or list_search_releases.")]
        Guid releaseId,
        [Description("The download client's id, from get_download_options.")]
        Guid downloadClientId,
        [Description("The destination's id, one of that client's destinations in get_download_options.")]
        Guid destinationId,
        [Description("An id of your own for this send, a new uuid; the same one on a retry of the same send. Left out, "
            + "MAM makes one up, and a retry can't be recognised as one.")]
        Guid? requestId = null,
        CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        // A retry first: the send answers it from the job its request made, whatever the folder is now (ADR 0013). Held
        // here, the retry of a send whose client was then cut off from the folder would tell the user nothing was sent
        // while the first try's torrent downloads — and every retry would ask the client again for nothing.
        if (requestId is not { } retried || !await downloads.IsRetryAsync(retried, userId, cancellationToken))
        {
            // Then, where the dialog disables Send: a folder its client can't see takes nothing (MAM-30, D5), and the
            // service would only write a job that fails. Refused before anything is written or sent, as the dialog holds
            // Send. Any other answer — a missing destination included — goes on to the send, whose own checks answer it.
            var check = await downloads.GetOverwriteCheckAsync(destinationId.ToString(), userId, cancellationToken);
            if (check.Answer?.NotCheckedReason == DownloadOverwriteCheck.NotCheckedFolderNotSeen)
                return McpToolResults.Error(texts.Render(McpToolErrors.SendHeldFolderNotSeen));
        }

        // Without a request id of the assistant's, every call is a new send: a retry can't be recognised (ADR 0013).
        var sent = await downloads.SendAsync(
            new SendDownloadRequest(requestId ?? Guid.NewGuid(), releaseId, downloadClientId, destinationId), userId, cancellationToken);
        // Created and Replayed carry the job, a failed one included — the HTTP API's 201 and 200 —, and the job says why it
        // failed; every refusal carries its sentence and wrote nothing.
        return sent.Job is { } job
            ? results.Success(texts.Render(await SendSummaryAsync(sent.Status, job, userId, cancellationToken)), job)
            : McpToolResults.Error(texts.Render(sent.Error!));
    }

    // ── summaries ───────────────────────────────────────────────────────────────────────────────

    /// <summary>MAM-47 — the dialog's own words for a failure without a reason (downloads.json failedNoReason).</summary>
    private static readonly Message NotSent = Message.Of("downloads.mcp.send.not-sent");

    /// <summary>What the dialog would show: how many clients, what it opens on, and how the destination checked
    /// fares.</summary>
    /// <remarks>MAM-47 (ADR 0042) — a sentence per pick, with or without the checked destination's clause: the pick is
    /// the object of "preselect" (design §2.3); the clause after "and" stands on its own, nested.</remarks>
    private static Message OptionsSummary(
        DownloadOptionsResponse options,
        DownloadClientOptionDto? client,
        DownloadDestinationOptionDto? destination,
        DownloadDestinationOptionDto? checkedDestination,
        DownloadOverwriteCheckResponse? check)
    {
        // The dialog's own sentence (downloads.json noClients).
        if (options.Clients.Count == 0)
            return Message.Of("downloads.mcp.options.no-clients");

        var count = options.Clients.Count;
        var tail = checkedDestination is null || check is null ? null : TailOf(checkedDestination, checkedDestination.Id == destination?.Id, check);
        return (client, destination, tail) switch
        {
            ({ } picked, { } into, null) =>
                Message.Of("downloads.mcp.options.pick-destination", ("count", count), ("destination", into.Name), ("client", picked.Name)),
            ({ } picked, { } into, { } more) =>
                Message.Of("downloads.mcp.options.pick-destination-tail", ("count", count), ("destination", into.Name), ("client", picked.Name), ("tail", more)),
            ({ } picked, null, null) => Message.Of("downloads.mcp.options.pick-client", ("count", count), ("client", picked.Name)),
            ({ } picked, null, { } more) => Message.Of("downloads.mcp.options.pick-client-tail", ("count", count), ("client", picked.Name), ("tail", more)),
            (_, _, null) => Message.Of("downloads.mcp.options.pick-none", ("count", count)),
            (_, _, { } more) => Message.Of("downloads.mcp.options.pick-none-tail", ("count", count), ("tail", more)),
        };
    }

    /// <summary>How the destination checked fares: "there" when it is the one the dialog would preselect, else by its
    /// name; why it goes unchecked by the check's API name.</summary>
    private static Message TailOf(DownloadDestinationOptionDto checkedDestination, bool there, DownloadOverwriteCheckResponse check) =>
        check.NotCheckedReason switch
        {
            null => there
                ? Message.Of("downloads.mcp.options.checked-there-clause")
                : Message.Of("downloads.mcp.options.checked-to-clause", ("destination", checkedDestination.Name)),
            DownloadOverwriteCheck.NotCheckedFolderNotSeen => there
                ? Message.Of("downloads.mcp.options.not-seen-there-clause")
                : Message.Of("downloads.mcp.options.not-seen-to-clause", ("destination", checkedDestination.Name)),
            { } reason => there
                ? Message.Of("downloads.mcp.options.unchecked-there-clause", ("reason", reason.ToString()))
                : Message.Of("downloads.mcp.options.unchecked-to-clause", ("destination", checkedDestination.Name), ("reason", reason.ToString())),
        };

    /// <summary>What came of the send; a failed one with why, and that the next try needs a new request id, as the
    /// dialog makes a new one after a Failed job.</summary>
    /// <remarks>MAM-47 (ADR 0042) — why is the job's own error as its answer shows it to this caller, nested as a message,
    /// so the request's language writes both (<see cref="IDownloadService.ReadAsync"/>).</remarks>
    private async Task<Message> SendSummaryAsync(DownloadSendStatus status, DownloadJobDto job, Guid userId, CancellationToken cancellationToken)
    {
        if (job.Status != DownloadJobStatus.Failed)
        {
            return status == DownloadSendStatus.Replayed
                ? Sent(job, "downloads.mcp.send.replayed", "downloads.mcp.send.replayed-into", ("status", job.Status.ToString()))
                : Sent(job, "downloads.mcp.send.sent", "downloads.mcp.send.sent-into", ("status", job.Status.ToString()));
        }

        var why = (await downloads.ReadAsync(job.Id, userId, cancellationToken))?.Error ?? NotSent;
        return status == DownloadSendStatus.Replayed
            ? Sent(job, "downloads.mcp.send.replayed-failed", "downloads.mcp.send.replayed-failed-into", ("why", why))
            : Sent(job, "downloads.mcp.send.failed", "downloads.mcp.send.failed-into", ("why", why));
    }

    /// <summary>The send's sentence <paramref name="key"/> naming the release and the client, or
    /// <paramref name="intoKey"/> naming the destination too, when the job names one.</summary>
    private static Message Sent(DownloadJobDto job, string key, string intoKey, (string Name, MessageValue Value) value) =>
        job.DestinationName is { } destination
            ? Message.Of(intoKey, ("release", job.ReleaseTitle), ("client", job.DownloadClientName), ("destination", destination), value)
            : Message.Of(key, ("release", job.ReleaseTitle), ("client", job.DownloadClientName), value);
}
