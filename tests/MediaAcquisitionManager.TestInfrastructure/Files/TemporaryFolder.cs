namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// A folder of its own under the system's temp folder for one test class (MAM-9), deleted with everything in
/// it — links included, which are removed as links and never followed.
/// </summary>
/// <remarks>
/// Not <see cref="Directory.Delete(string, bool)"/>: on Windows it asks the system to unmount every junction it
/// meets, which only an administrator may do, so it fails on a junction it has in fact removed.
/// </remarks>
public sealed class TemporaryFolder : IDisposable
{
    /// <param name="prefix">The start of the folder's name (<c>mam-…</c>), so a leftover says whose it is.</param>
    public TemporaryFolder(string prefix) => FullName = Directory.CreateTempSubdirectory(prefix).FullName;

    /// <summary>The folder's full path.</summary>
    public string FullName { get; }

    /// <summary>The path of <paramref name="names"/> inside the folder.</summary>
    public string PathOf(params string[] names) => Path.Combine([FullName, .. names]);

    /// <summary>Creates the folder <paramref name="names"/> (and those above it) and returns its path.</summary>
    public string CreateFolder(params string[] names) => Directory.CreateDirectory(PathOf(names)).FullName;

    /// <summary>Writes <paramref name="content"/> into the file <paramref name="names"/>, creating its folder, and
    /// returns its path.</summary>
    public string WriteFile(string content, params string[] names)
    {
        var path = PathOf(names);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => DeleteTree(FullName);

    private static void DeleteTree(string folder)
    {
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if (entry.LinkTarget is not null)
            {
                // A link to a folder is removed like a folder on Windows and like a file on Unix.
                if (OperatingSystem.IsWindows() && entry.Attributes.HasFlag(FileAttributes.Directory))
                    Directory.Delete(entry.FullName);
                else
                    File.Delete(entry.FullName);
            }
            else if (entry is DirectoryInfo)
            {
                DeleteTree(entry.FullName);
            }
            else
            {
                File.Delete(entry.FullName);
            }
        }

        Directory.Delete(folder);
    }
}
