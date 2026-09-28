namespace MediaAcquisitionManager.TestInfrastructure;

/// <summary>
/// Files in the repository that tests read, found from wherever the test assembly runs — a
/// hardcoded relative path breaks the moment the output directory layout or the configuration changes.
/// </summary>
public static class RepositoryPaths
{
    private const string SolutionFileName = "MediaAcquisitionManager.slnx";

    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    /// <summary>The directory holding <c>MediaAcquisitionManager.slnx</c>.</summary>
    public static string Root => RootDirectory.Value;

    /// <summary>The SPA shell source: what the API serves at every client-side route, and where the
    /// inline theme script whose hash the CSP pins lives.</summary>
    public static string FrontendIndexHtml => Path.Combine(Root, "frontend", "index.html");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"No directory above {AppContext.BaseDirectory} contains {SolutionFileName}; the tests must run from a checkout of the repository.");
    }
}
