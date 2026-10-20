using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// Where a service path is inside the app, if anywhere. Only <see cref="PathMapStatus.Identity"/> and
/// <see cref="PathMapStatus.Mapped"/> give a path the app may look at, and neither proves it is the
/// same file: device and inode decide that (ADR 0003).
/// </summary>
/// <param name="Status">How the path mapped.</param>
/// <param name="AppPath">The app's path in canonical form; <c>null</c> unless
/// <see cref="HasAppPath"/>.</param>
/// <param name="Reason">Why there is no app path, as a message the Api renders (ADR 0042); <c>null</c> when there
/// is one.</param>
public sealed record PathMapResult(PathMapStatus Status, string? AppPath, Message? Reason)
{
    /// <summary>Whether <see cref="AppPath"/> is a path the app may look at.</summary>
    public bool HasAppPath => Status is PathMapStatus.Identity or PathMapStatus.Mapped;
}
