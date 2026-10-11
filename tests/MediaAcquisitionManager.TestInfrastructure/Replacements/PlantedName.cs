using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>MAM-26 — one name a planted replacement's move planned (<see cref="ReplacementQuarantinePlanter"/>).</summary>
/// <param name="Kind">A ticked copy's video — which starts a copy of its own — or a companion of the video before it.</param>
/// <param name="AppPath">Where it was in the library, the app's, inside <see cref="ReplacementVerificationHarness.MediaRoot"/>.</param>
/// <param name="Facts">Its identity, read right before the move.</param>
/// <param name="State">Where it stands; the file is planted where that says it is.</param>
/// <param name="Problem">Why it stayed, wasn't put back or deleted, or was lost.</param>
public sealed record PlantedName(
    ReplacementNameKind Kind,
    string AppPath,
    FileFacts Facts,
    ReplacementNameState State = ReplacementNameState.Quarantined,
    string? Problem = null);
