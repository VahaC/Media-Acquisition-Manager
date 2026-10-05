namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// A folder the platform's reader has opened for the walk of the allowed roots (MAM-18, ADR 0021): the path that
/// lists this very folder, and its identity where the platform gives one. Kept open while its entries are read
/// and judged, so on Linux a link swapped in at its path meanwhile can't put another folder in its place.
/// </summary>
internal interface IOpenedFolder : IDisposable
{
    /// <summary>
    /// What the folder's entries are listed through: <c>/proc/self/fd/&lt;n&gt;</c> on Linux, the folder opened
    /// here whatever its path leads to by then; the folder's own path elsewhere.
    /// </summary>
    string ListPath { get; }

    /// <summary>The device the folder is on, with <see cref="Inode"/>; <c>null</c> where the platform gives no
    /// identity, and then the walk tells a loop by the folder's resolved path.</summary>
    long? DeviceId { get; }

    /// <summary>The folder's inode on <see cref="DeviceId"/>; <c>null</c> with it.</summary>
    long? Inode { get; }
}
