namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// The real ffprobe on this machine's PATH, if there is one (MAM-9): the few tests that run it skip where it
/// isn't installed, such as the CI build container.
/// </summary>
public static class InstalledFfprobe
{
    private static readonly Lazy<string?> Found = new(Find);

    /// <summary>The full path of ffprobe, or <c>null</c> when the PATH has none.</summary>
    public static string? FullPath => Found.Value;

    private static string? Find()
    {
        var name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
    }
}
