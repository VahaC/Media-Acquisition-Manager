using System.ComponentModel;
using System.Security.Claims;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Services.PersonalAccessTokens;
using MediaAcquisitionManager.Api.Texts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MediaAcquisitionManager.Api.Mcp.Tools;

/// <summary>MAM-45 — <c>list_jobs</c>, <c>get_job</c> and <c>cancel_job</c>: the web app's Jobs page, the caller's own
/// jobs only.</summary>
/// <remarks>
/// <see cref="IDownloadService"/> scopes every job to its sender, as for the Jobs page: another user's job answers as a
/// missing one (<see cref="McpToolErrors.JobNotFound"/>), the owner's to a member too. Nothing here asks the download
/// client: the jobs are what the poller last read, and a cancel only stops following (ADR 0014). Built per call by the
/// SDK, with the request's services. MAM-47 (ADR 0042) — every sentence in the language of the MCP request
/// (<see cref="IRequestTexts"/>): English unless the client sends <c>Accept-Language</c>.
/// </remarks>
[McpServerToolType]
public sealed class JobTools(
    IDownloadService downloads, IPersonalAccessTokenRefusals personalAccessTokenRefusals, IRequestTexts texts, McpToolResults results)
{
    /// <summary>The tool a refused cancel is recorded under, as the access gate records a tool's name.</summary>
    private const string CancelJob = "cancel_job";

    /// <summary>The files <c>get_job</c> lists: the Jobs page's first page of them.</summary>
    private const int FirstFiles = 25;

    [McpServerTool(
        Name = "list_jobs",
        Title = "List download jobs",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DownloadJobListResponse))]
    [Description(
        "Lists your download jobs, newest first, as MAM last read them from the download client: each with its status, "
        + "progress, release, client and destination, and the error or the reason when one stopped. view picks a tab of "
        + "the web app's Jobs page and status a single status; needsYouCount and inProgressCount count all your jobs "
        + "whatever the filters. Only your own jobs are listed.")]
    public async Task<CallToolResult> ListJobsAsync(
        ClaimsPrincipal user,
        [Description("NeedsYou: stopped and waiting for someone, such as a download that needs linking; InProgress: MAM "
            + "is still working on it; Finished: nothing more happens to it.")]
        DownloadJobView? view = null,
        [Description("Keep only the jobs in this status.")]
        DownloadJobStatus? status = null,
        [Description("The page, from 1.")]
        int page = 1,
        [Description("Jobs per page, 1 to 100.")]
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // By name, as the Jobs page sends them: the service reads a view and a status by name alone.
        var listed = await downloads.ListAsync(status?.ToString(), view?.ToString(), page, pageSize, user.GetUserId(), cancellationToken);
        if (listed.List is not { } list)
            return McpToolResults.Error(texts.Render(listed.Error!));

        return results.Success(
            texts.Render(Message.Of(
                "downloads.mcp.jobs.listed",
                ("count", list.TotalCount),
                ("shown", list.Items.Count),
                ("needsYou", list.NeedsYouCount),
                ("inProgress", list.InProgressCount))),
            list);
    }

    [McpServerTool(
        Name = "get_job",
        Title = "Download job",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(JobToolResult))]
    [Description(
        "One of your download jobs: where it stands, why it stopped if it did, and its first 25 files with their sizes "
        + "and progress, as MAM last read them from the download client.")]
    public async Task<CallToolResult> GetJobAsync(
        ClaimsPrincipal user,
        [Description("The job's id, from list_jobs or send_download.")]
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        // MAM-47 — with the job's sentences as messages, which the summary nests (ADR 0042).
        if (await downloads.ReadAsync(jobId, userId, cancellationToken) is not { } read)
            return McpToolResults.Error(texts.Render(McpToolErrors.JobNotFound));
        // Page 1 of 25 always fits the list's rules, so only a job gone since the read above — its sender's account
        // removed — answers otherwise, as a missing job.
        if ((await downloads.ListFilesAsync(jobId, 1, FirstFiles, userId, cancellationToken)).List is not { } files)
            return McpToolResults.Error(texts.Render(McpToolErrors.JobNotFound));

        return results.Success(texts.Render(JobSummary(read, files)), new JobToolResult(read.Job, files));
    }

    [McpServerTool(
        Name = CancelJob,
        Title = "Cancel a download job",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DownloadJobDto))]
    [PersonalAccessTokenWrite]
    [Description(
        "Stops MAM following one of your download jobs: it becomes Cancelled, and the release may be sent again. Nothing "
        + "is sent to the download client — the torrent and its files stay there as they are, for the user to remove "
        + "there if they want. A job that already ended, or one still being sent, can't be cancelled; a job that also "
        + "replaces a copy in the library is cancelled in the web app only. Needs an access token with Read and download "
        + "access.")]
    public async Task<CallToolResult> CancelJobAsync(
        ClaimsPrincipal user,
        [Description("The job's id, from list_jobs or send_download.")]
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId();
        // As DownloadsController.Cancel does for a token: a token never touches a replacement (ADR 0040), so a cancel that
        // would end one too changes nothing, and is refused and recorded the HTTP API's way.
        var cancelled = await downloads.CancelAsync(jobId, userId, mayEndReplacement: false, cancellationToken);
        if (cancelled.Status == DownloadCancelStatus.ReplacementNeedsTheWebApp)
        {
            // Always a token: /mcp refuses a session before any tool runs.
            var tokenId = user.GetPersonalAccessTokenId()
                ?? throw new InvalidOperationException("cancel_job runs on a personal access token only.");
            await personalAccessTokenRefusals.RecordAsync(
                tokenId, userId, PersonalAccessTokenRefusalReason.CancelEndsReplacement, McpToolAccessFilter.CallMethod, CancelJob, cancellationToken);
            return McpToolResults.Error(texts.Render(PersonalAccessTokenErrors.CancelEndsReplacement));
        }

        return cancelled.Status switch
        {
            DownloadCancelStatus.Cancelled => results.Success(
                texts.Render(Message.Of(
                    "downloads.mcp.jobs.cancelled", ("release", cancelled.Job!.ReleaseTitle), ("client", cancelled.Job.DownloadClientName))),
                cancelled.Job),
            DownloadCancelStatus.NothingToStop or DownloadCancelStatus.StillBeingSent => McpToolResults.Error(texts.Render(cancelled.Error!)),
            _ => McpToolResults.Error(texts.Render(McpToolErrors.JobNotFound)),
        };
    }

    // ── summaries ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Where the job stands, why it stopped if it did, and how many of its files the answer lists.</summary>
    /// <remarks>MAM-47 (ADR 0042) — a sentence per shape, with or without the progress and the job's own sentences (design
    /// §2.3): how many files are listed is a clause of its own, the job's error and block reason are nested as the
    /// messages its answer renders, and the status stays its API name.</remarks>
    private static Message JobSummary(DownloadJobRead read, DownloadJobFileListResponse files)
    {
        var job = read.Job;
        var listed = files.TotalCount switch
        {
            0 => Message.Of("downloads.mcp.job.listed.none-clause"),
            var total when total <= files.Items.Count => Message.Of("downloads.mcp.job.listed.all-clause", ("count", total)),
            var total => Message.Of("downloads.mcp.job.listed.first-clause", ("shown", files.Items.Count), ("count", total)),
        };
        // The job's own sentences, each a full one: why it failed, why a followed one isn't moving on.
        MessageValue[] sentences = [.. new[] { read.Error, read.BlockReason }.OfType<Message>()];
        var why = sentences.Length > 0 ? new ListValue(sentences) : null;
        (string Name, MessageValue Value)[] values =
            [("release", job.ReleaseTitle), ("client", job.DownloadClientName), ("status", job.Status.ToString()), ("listed", listed)];
        // Rounded down: a download at 99.6 % isn't done. A number value is a decimal, so a progress that is no finite
        // number — no client reports one — shows none rather than failing the answer.
        if (job.Progress is { } done && double.IsFinite(done))
        {
            var percent = (decimal)Math.Floor(done * 100);
            return why is null
                ? Message.Of("downloads.mcp.job.summary-progress", [.. values, ("percent", percent)])
                : Message.Of("downloads.mcp.job.summary-progress-why", [.. values, ("percent", percent), ("why", why)]);
        }

        return why is null
            ? Message.Of("downloads.mcp.job.summary", values)
            : Message.Of("downloads.mcp.job.summary-why", [.. values, ("why", why)]);
    }
}
