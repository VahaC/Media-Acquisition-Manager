using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26, MAM-40 — what an owner's write does to each name in the quarantine (the Strategy
/// <see cref="QuarantineWriteLoop{TName}"/> runs): Restore's or Delete for good's states, audit actions, gate, call, time
/// stamp and stop sentence (<see cref="QuarantineNameWrites"/>), the same for a replacement's names and a removal's but for
/// the audit actions.
/// </summary>
/// <param name="NameRunning">A name's state between its intent and its outcome.</param>
/// <param name="NameEnd">A name's state once written.</param>
/// <param name="NameIntentAction">A name's row before the call.</param>
/// <param name="NameDoneAction">A name's row once written.</param>
/// <param name="NameNotDoneAction">A name's row when the call refused.</param>
/// <param name="TellsLibrary">Whether the library is told of the names written (a restore; a deletion was told at the
/// move).</param>
/// <param name="SwitchedOffError">ADR 0033 — the {error} of a stop when the switch was turned off between two names.</param>
/// <param name="Closed">The write's own gate on a name, read again before each: the {error} of a stop, or <c>null</c>.</param>
/// <param name="WriteAsync">The one call through <see cref="IFileQuarantine"/>, within the roots given.</param>
/// <param name="Stamp">Records when a name was written.</param>
/// <param name="SwappedProblem">A name's problem when its write moved another file put at it after the last look.</param>
/// <param name="StopReason">The owner row's reason when a name stopped it: the name, the {error}, how many of how many
/// were written (<see cref="ReplacementNameTally"/>).</param>
internal sealed record QuarantineNameWrite(
    ReplacementNameState NameRunning,
    ReplacementNameState NameEnd,
    AuditAction NameIntentAction,
    AuditAction NameDoneAction,
    AuditAction NameNotDoneAction,
    bool TellsLibrary,
    string SwitchedOffError,
    Func<QuarantinedNameEntity, ReplacementGateState, string?> Closed,
    Func<QuarantinedNameEntity, IReadOnlyList<string>, Task<FileQuarantineResult<FileFacts>>> WriteAsync,
    Action<QuarantinedNameEntity, DateTime> Stamp,
    Func<QuarantinedNameEntity, string> SwappedProblem,
    Func<QuarantinedNameEntity, string, int, int, string> StopReason);
