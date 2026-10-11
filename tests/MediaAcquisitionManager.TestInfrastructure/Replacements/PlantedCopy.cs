using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-27 — one ticked old copy a planted replacement holds with no name of a move (<see cref="ReplacementQuarantinePlanter"/>):
/// as the check of the new files left it, still at its old path.
/// </summary>
/// <param name="AppPath">Where it is in the library, the app's, inside <see cref="ReplacementVerificationHarness.MediaRoot"/>.</param>
/// <param name="Facts">Its identity, read when it was scheduled; the file is planted there with it.</param>
/// <param name="Problem">Why it stopped the job.</param>
/// <param name="OnlyDropSettles">Whether <paramref name="Problem"/> is one only a drop settles.</param>
/// <param name="DroppedUtc">When the owner dropped it from the job.</param>
/// <param name="PairedWithNewFile">The place, in the planted new files, of the one it is paired with.</param>
/// <param name="SeasonNumber">An episode's season when it was scheduled.</param>
/// <param name="EpisodeNumber">An episode's number there.</param>
public sealed record PlantedCopy(
    string AppPath,
    FileFacts Facts,
    string? Problem = null,
    bool OnlyDropSettles = false,
    DateTime? DroppedUtc = null,
    int? PairedWithNewFile = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null);
