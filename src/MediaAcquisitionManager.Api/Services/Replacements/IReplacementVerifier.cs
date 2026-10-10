namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — checks each new file of a replacement whose download is done before anything is touched (specification §3.4,
/// checks 1–4; ADR 0032, ADR 0035): complete by the client's word, readable by the media inspector inside the allowed
/// folders, listed and finished by the library once asked to look, and identified by the library's ids as the old copy's
/// own work, every ticked copy paired with exactly one new file. Then the job is <c>Verified</c>, or stops —
/// <c>NeedsReview</c>, or <c>Failed</c> for a bad file — with its reason, both copies staying. Nothing is moved in this
/// version: moving the old copies aside is MAM-26's.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Downloaded</c> job is claimed first (<c>Inspecting</c>, a <c>ReplacementCheckStarted</c> row); then on every pass
/// it goes on from its rows: the files' checks until they pass, the library asked to look once, then the library read on
/// every pass until the pairing decides. What can't be read now waits — an hour from the check's start for the files, an
/// hour from the request for the library (<c>ReplacementVerificationRules</c>) — and stops the job for review once it
/// ran out: an unavailable client or library never lets a copy go. Every decision is a conditional move with its audit
/// row, no actor (the app's own check), and the evidence of each check is kept with the job.
/// </para>
/// <para>
/// It runs whatever the replacement switch and the ticks say (ADR 0033: scheduled jobs keep their checks and move
/// nothing). Every external call is a read but the one request to look; the download client is only read.
/// </para>
/// </remarks>
public interface IReplacementVerifier
{
    /// <summary>
    /// Takes every replacement being checked (<c>ReplacementJobStates.Verifying</c>) one step on, each on its own: one that
    /// breaks is logged and the others go on. Only a cancellation by the caller propagates.
    /// <c>ReplacementVerificationTracker</c> calls it on each of its passes.
    /// </summary>
    Task VerifyAsync(CancellationToken cancellationToken = default);
}
