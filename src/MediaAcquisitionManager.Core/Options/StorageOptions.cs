namespace MediaAcquisitionManager.Core.Options;

/// <summary>
/// Where the installation keeps its state — the SQLite database and the first-run secrets. Bound
/// from the <c>Storage</c> section (<c>MAM_Storage__DataDirectory</c> in the environment) and turned
/// into absolute paths once, at startup, by <see cref="Storage.StorageLayout.Resolve"/>.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// The directory used when none is configured: <c>Data</c> under the content root, so a fresh
    /// checkout runs with zero configuration and keeps its state out of the build output.
    /// </summary>
    public const string DefaultDataDirectory = "Data";

    /// <summary>
    /// The data directory. A relative path is resolved against the content root, not the working
    /// directory, so the app finds the same database however it was started.
    /// </summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory;
}
