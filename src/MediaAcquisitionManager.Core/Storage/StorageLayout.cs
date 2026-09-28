using MediaAcquisitionManager.Core.Options;

namespace MediaAcquisitionManager.Core.Storage;

/// <summary>
/// The absolute paths of everything the installation persists. Resolved once at startup, so the
/// DbContext, the first-run secrets and anything added later agree on one location instead of each
/// re-reading configuration and resolving a relative path against a different base.
/// </summary>
/// <param name="DataDirectory">The absolute data directory.</param>
/// <param name="DatabasePath">The SQLite file inside it.</param>
/// <param name="SecretsDirectory">The directory holding the generated first-run secrets.</param>
public sealed record StorageLayout(string DataDirectory, string DatabasePath, string SecretsDirectory)
{
    /// <summary>The SQLite file name inside the data directory.</summary>
    public const string DatabaseFileName = "mam.db";

    /// <summary>The first-run secrets directory name inside the data directory. A dot directory,
    /// so a casual <c>ls</c> of the data volume doesn't put key files on screen.</summary>
    public const string SecretsDirectoryName = ".secrets";

    /// <summary>
    /// Resolves <paramref name="options"/> against <paramref name="contentRootPath"/> (which must be
    /// absolute): a relative data directory lands under the content root, an absolute one is used as
    /// is, and both come back normalised.
    /// </summary>
    public static StorageLayout Resolve(StorageOptions options, string contentRootPath)
    {
        // A blank value (an empty MAM_Storage__DataDirectory) means "not configured". Resolving it
        // literally would put the database and the secrets straight into the content root, among the
        // app's own files, where the next deployment of the app overwrites or deletes them.
        var configured = string.IsNullOrWhiteSpace(options.DataDirectory)
            ? StorageOptions.DefaultDataDirectory
            : options.DataDirectory.Trim();

        var dataDirectory = Path.GetFullPath(configured, contentRootPath);
        return new StorageLayout(
            dataDirectory,
            Path.Combine(dataDirectory, DatabaseFileName),
            Path.Combine(dataDirectory, SecretsDirectoryName));
    }
}
