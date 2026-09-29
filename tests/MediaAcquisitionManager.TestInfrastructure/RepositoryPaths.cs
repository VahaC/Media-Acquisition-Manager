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

    /// <summary>The image definition: its HEALTHCHECK probes the app, and it sets the data directory.</summary>
    public static string Dockerfile => Path.Combine(Root, "Dockerfile");

    /// <summary>The Compose file that runs the published image.</summary>
    public static string DockerCompose => Path.Combine(Root, "docker-compose.yml");

    /// <summary>The Compose file that builds the image from the checkout.</summary>
    public static string DockerComposeBuild => Path.Combine(Root, "docker-compose.build.yml");

    /// <summary>The documented list of every variable an installation can set.</summary>
    public static string EnvExample => Path.Combine(Root, ".env.example");

    /// <summary>The API's base settings: every setting the app reads, secrets included as <c>null</c>.</summary>
    public static string ApiAppSettings => Path.Combine(Root, "src", "MediaAcquisitionManager.Api", "appsettings.json");

    /// <summary>Real answers of Jellyfin, qBittorrent, Prowlarr's Torznab feed and ffprobe from the
    /// reference installation, sanitized (MAM-6): what the adapters are tested against.</summary>
    public static string VendorSamples => Path.Combine(Root, "tests", "MediaAcquisitionManager.TestInfrastructure", "VendorSamples");

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
