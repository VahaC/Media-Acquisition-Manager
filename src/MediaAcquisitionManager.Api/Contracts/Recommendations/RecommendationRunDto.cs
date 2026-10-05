using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — one computation of the recommendations, from the request to its outcome.</summary>
/// <param name="Id">The run's id.</param>
/// <param name="Status">Where it stands; <c>Queued</c> and <c>Running</c> are under way.</param>
/// <param name="Stage">What the running run is doing; <c>null</c> while it isn't running, and before its first
/// stage.</param>
/// <param name="StageDone">How much of a counted stage is done ("12 of 40"), written at most every two seconds and at
/// the stage's end; <c>null</c> while it isn't running, and for a stage that isn't counted.</param>
/// <param name="StageTotal">What the counted stage has to do in all; <c>null</c> with <paramref name="StageDone"/>.</param>
/// <param name="Trigger">What queued it.</param>
/// <param name="RequestedByUserName">Who asked for it. For the installation owner only, like a sync's: <c>null</c>
/// for everyone else, and when the app queued it.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="StartedUtc">When the worker took it; <c>null</c> while queued.</param>
/// <param name="FinishedUtc">When it succeeded or failed; <c>null</c> while under way.</param>
/// <param name="SeedCount">How many catalog works of the library it started from; <c>null</c> unless it
/// succeeded.</param>
/// <param name="CandidateCount">How many works it recommends, for every user together; <c>null</c> unless it
/// succeeded.</param>
/// <param name="Error">Why it failed. For the installation owner only — the sentence can name the catalog's
/// address: <c>null</c> for everyone else.</param>
public sealed record RecommendationRunDto(
    Guid Id,
    RecommendationRunStatus Status,
    RecommendationRunStage? Stage,
    int? StageDone,
    int? StageTotal,
    RecommendationRunTrigger Trigger,
    string? RequestedByUserName,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int? SeedCount,
    int? CandidateCount,
    string? Error);
