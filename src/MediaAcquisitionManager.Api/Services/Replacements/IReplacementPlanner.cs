using MediaAcquisitionManager.Api.Services.Releases;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the plan of a replacement the owner schedules (ADR 0032, ADR 0033): the same works, copies and reasons the
/// Download dialog read (<see cref="IReplacementCandidateReader"/>), read again when the schedule comes, and the ticks
/// checked against them — then what to write with the download job: each ticked copy with its identity fresh from the
/// disk, and each work it is a copy of with its registry ids.
/// </summary>
/// <remarks>
/// <para>
/// R1, ADR 0013 — from the database and the disk alone, never a download client: the job is written before anything is
/// fetched or sent, so the plan can't ask the client anything. The dialog's read and the plan differ in exactly that:
/// the destination's checks — where its category saves, inside a library and the Media folders — are the dialog's
/// before Send and the sender's strict mode after the job is written (a <c>Failed</c> job, decision 7); the torrents'
/// held marks are the dialog's alone.
/// </para>
/// <para>
/// It checks nothing about who calls: the route is the owner's alone by where its controller lives
/// (<c>ReplacementsAreOwnerOnlyConvention</c>). Writes nothing.
/// </para>
/// </remarks>
public interface IReplacementPlanner
{
    /// <summary>
    /// What to write for <paramref name="request"/> on <paramref name="found"/>, one of the caller's releases: refused as
    /// <see cref="ReplacementPlanStatus.Unavailable"/> when replacement is off, the release a magnet link or matching no
    /// work, or a ticked copy can't be replaced — its work's reason or its own, the disk's answer now included —; as
    /// <see cref="ReplacementPlanStatus.Invalid"/> when a ticked id isn't a copy the release offers, a confirmation it
    /// needs isn't ticked, or one ticked isn't needed or has no copy ticked.
    /// </summary>
    Task<ReplacementPlanResult> PlanAsync(CallerRelease found, ReplacementRequest request, CancellationToken cancellationToken = default);
}
