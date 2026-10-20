using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-47 (ADR 0042) — a job as its answer shows it, with the two sentences that answer renders, as messages: an MCP
/// tool's summary nests them, so the request's language writes the summary and them alike.
/// </summary>
/// <param name="Job">The job as <see cref="IDownloadService.GetAsync"/> answers it.</param>
/// <param name="Error">Why it failed, as the reader may read it (the owner the service's own words, anyone else the
/// app's); <c>null</c> without an error.</param>
/// <param name="BlockReason">Why a followed job isn't moving on, by the same rule; <c>null</c> when nothing blocks
/// it.</param>
public sealed record DownloadJobRead(DownloadJobDto Job, Message? Error, Message? BlockReason);
