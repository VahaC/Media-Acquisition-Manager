using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>One entry of a walk through the allowed roots (MAM-18): a media file name, or a place the walk
/// didn't go into or through, with why.</summary>
/// <param name="Status">What the entry is.</param>
/// <param name="AppPath">The name's or the place's path inside the app, in canonical form, under the root
/// as the owner typed it (a link's own path, not where it leads).</param>
/// <param name="ResolvedPath">For a <see cref="FileListingStatus.MediaFile"/>, where the file really is: the
/// same path with every link on the way followed, in canonical form — two names reaching one file through
/// a link share it. <c>null</c> for every other status.</param>
/// <param name="Error">Why the walk didn't go there, as a message the Api renders (ADR 0042); <c>null</c> for a media
/// file.</param>
public sealed record FileListingEntry(FileListingStatus Status, string AppPath, string? ResolvedPath, Message? Error);
