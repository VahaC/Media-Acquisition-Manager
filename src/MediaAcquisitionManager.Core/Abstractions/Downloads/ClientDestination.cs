using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>One of a download client's own destinations, such as a category or a folder.</summary>
/// <param name="Key">The client's name for it: what a submission sends. MAM-30 — for a folder, the folder as the client
/// sees it, in <see cref="Paths.NormalizedPath"/> canonical form.</param>
/// <param name="DisplayName">What to show for it.</param>
/// <param name="ServicePath">The folder it saves to as the client sees it; <c>null</c> when the client
/// doesn't say.</param>
/// <param name="IsClientDefaultFolder">MAM-30 — whether it is the folder the client saves to when it is told none
/// (Transmission's <c>download-dir</c>); false for a category and for any other folder.</param>
/// <param name="Problem">MAM-30 — why the client can't use this folder (it can't see it, or it isn't a path it takes),
/// as a message the Api renders (ADR 0042), every secret masked; <c>null</c> when it can. A destination with a problem is
/// listed, so the owner learns why, but is never picked or sent to.</param>
public sealed record ClientDestination(
    string Key,
    string DisplayName,
    string? ServicePath,
    bool IsClientDefaultFolder = false,
    Message? Problem = null);
