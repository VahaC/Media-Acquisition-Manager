using System.ComponentModel;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Enums;
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
/// </remarks>
[McpServerToolType]
public sealed class DownloadTools(IDownloadService downloads, McpToolResults results)
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
            return McpToolResults.Error(listed.Error!);

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
                return McpToolResults.Error(DownloadToolErrors.DestinationNotOffered);
        }

        // As the dialog asks once a destination is picked: one live question to its client, and none without a pick.
        DownloadOverwriteCheckResponse? check = null;
        if (checkedDestination is not null)
        {
            var answered = await downloads.GetOverwriteCheckAsync(checkedDestination.Id.ToString(), userId, cancellationToken);
            if (answered.Answer is not { } answer)
                return McpToolResults.Error(answered.Error!);
            check = answer;
        }

        var held = check?.NotCheckedReason == DownloadOverwriteCheck.NotCheckedFolderNotSeen;
        return results.Success(
            OptionsSummary(options, client, destination, checkedDestination, check),
            new DownloadOptionsToolResult(
                options, client?.Id, destination?.Id, checkedDestination?.Id, check, held, held ? McpToolErrors.SendHeldFolderNotSeen : null));
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
                return McpToolResults.Error(McpToolErrors.SendHeldFolderNotSeen);
        }

        // Without a request id of the assistant's, every call is a new send: a retry can't be recognised (ADR 0013).
        var sent = await downloads.SendAsync(
            new SendDownloadRequest(requestId ?? Guid.NewGuid(), releaseId, downloadClientId, destinationId), userId, cancellationToken);
        // Created and Replayed carry the job, a failed one included — the HTTP API's 201 and 200 —, and the job says why it
        // failed; every refusal carries its sentence and wrote nothing.
        return sent.Job is { } job
            ? results.Success(SendSummary(sent.Status, job), job)
            : McpToolResults.Error(sent.Error!);
    }

    // ── summaries ───────────────────────────────────────────────────────────────────────────────

    /// <summary>What the dialog would show: how many clients, what it opens on, and how the destination checked
    /// fares.</summary>
    private static string OptionsSummary(
        DownloadOptionsResponse options,
        DownloadClientOptionDto? client,
        DownloadDestinationOptionDto? destination,
        DownloadDestinationOptionDto? checkedDestination,
        DownloadOverwriteCheckResponse? check)
    {
        // The dialog's own sentence (downloads.json noClients).
        if (options.Clients.Count == 0)
            return "No download client can take this release yet. The owner sets them up under Integrations and Destinations.";

        var pick = (client, destination) switch
        {
            ({ } picked, { } into) => $"“{into.Name}” of “{picked.Name}”",
            ({ } picked, null) => $"“{picked.Name}” with no destination",
            _ => "no client",
        };
        var summary = $"{Count(options.Clients.Count, "download client")} can take this release; the Download dialog would preselect {pick}";
        if (checkedDestination is null || check is null)
            return summary + ".";

        var where = checkedDestination.Id == destination?.Id ? "there" : $"to “{checkedDestination.Name}”";
        return summary + check.NotCheckedReason switch
        {
            null => $", and a release sent {where} is checked for files it would write over.",
            DownloadOverwriteCheck.NotCheckedFolderNotSeen => $", and nothing can be sent {where}: its download client can't see the folder.",
            { } reason => $", and a release sent {where} goes without the check for files it would write over ({reason}).",
        };
    }

    /// <summary>What came of the send; a failed one with why, and that the next try needs a new request id, as the
    /// dialog makes a new one after a Failed job.</summary>
    private static string SendSummary(DownloadSendStatus status, DownloadJobDto job)
    {
        var what = $"“{job.ReleaseTitle}” to “{job.DownloadClientName}”" + (job.DestinationName is { } name ? $", into “{name}”," : "");
        // The dialog's own words for a failure without a reason (downloads.json failedNoReason).
        var why = job.Error ?? "The release wasn't sent.";
        return (status, job.Status) switch
        {
            (DownloadSendStatus.Replayed, DownloadJobStatus.Failed) =>
                $"This requestId already tried to send {what} and that send failed: {why} A new send needs a new requestId.",
            (DownloadSendStatus.Replayed, _) => $"This requestId already sent {what} so nothing was sent again; the job is {job.Status}.",
            (_, DownloadJobStatus.Failed) => $"Sending {what} failed: {why} A new send needs a new requestId.",
            _ => $"Sent {what} and the job is {job.Status}; list_jobs and get_job follow it.",
        };
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
