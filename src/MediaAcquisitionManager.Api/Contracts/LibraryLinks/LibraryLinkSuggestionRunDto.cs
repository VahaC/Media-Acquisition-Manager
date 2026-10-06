using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one preparation of the TMDb suggestions for the library works without a TMDb id.</summary>
/// <param name="Id">The run's id.</param>
/// <param name="Status">Where it stands; <c>Queued</c> and <c>Running</c> are under way.</param>
/// <param name="Trigger">What queued it.</param>
/// <param name="RequestedByUserName">Who asked for it; <c>null</c> when the app queued it.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="StartedUtc">When the worker took it; <c>null</c> while queued.</param>
/// <param name="FinishedUtc">When it succeeded or failed; <c>null</c> while under way.</param>
/// <param name="WorksTotal">How many works it searches; <c>null</c> until counted.</param>
/// <param name="WorksDone">How many it has searched so far.</param>
/// <param name="Error">Why it failed; <c>null</c> unless it did.</param>
public sealed record LibraryLinkSuggestionRunDto(
    Guid Id,
    LibraryLinkSuggestionRunStatus Status,
    LibraryLinkSuggestionRunTrigger Trigger,
    string? RequestedByUserName,
    DateTime CreatedUtc,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    int? WorksTotal,
    int WorksDone,
    string? Error);
