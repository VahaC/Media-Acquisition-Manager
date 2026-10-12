using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-40 — what <see cref="QuarantineCopyCheck"/> found of a copy about to move into the quarantine: where it
/// is, the allowed root whose quarantine takes it and what the disk says of it now — or why it stays.</summary>
/// <param name="Stop">Why it stays; <c>null</c> when it may move.</param>
/// <param name="Error">The disk's own sentence, for <see cref="QuarantineCopyStop.NotRead"/>.</param>
/// <param name="AppPath">Its path, parsed; set when it may move.</param>
/// <param name="RootText">The allowed root as stored, whose quarantine takes it; set when it may move.</param>
/// <param name="Root">That root, parsed; set when it may move.</param>
/// <param name="Fresh">What the disk said of it during this look; set when it may move.</param>
public sealed record QuarantineCopyLook(
    QuarantineCopyStop? Stop, string? Error, NormalizedPath? AppPath, string? RootText, NormalizedPath? Root, FileFacts? Fresh)
{
    /// <summary>The copy stays, for <paramref name="stop"/>.</summary>
    public static QuarantineCopyLook Stopped(QuarantineCopyStop stop, string? error = null) => new(stop, error, null, null, null, null);

    /// <summary>The copy may move: at <paramref name="appPath"/> inside <paramref name="root"/>, as <paramref name="fresh"/>
    /// says it is now.</summary>
    public static QuarantineCopyLook Found(NormalizedPath appPath, string rootText, NormalizedPath root, FileFacts fresh) =>
        new(null, null, appPath, rootText, root, fresh);
}
