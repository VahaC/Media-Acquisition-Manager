using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// A <see cref="NormalizedPath"/> in the form the operating system opens (MAM-9): exactly the folder names
/// the checks looked at, with nothing reinterpreted on the way.
/// </summary>
/// <remarks>
/// A POSIX path goes as it is: the kernel takes every name literally, and <c>.</c> and <c>..</c> are refused
/// before a path gets here. A Windows path gets the extended-length prefix <c>\\?\</c>, because without it
/// Win32 rewrites a path before opening it — it drops trailing dots and spaces from names (so
/// <c>D:\Media\.. \x</c> opens <c>D:\x</c>, though no folder name in it is <c>..</c>) and maps names like
/// <c>CON</c> to devices. With the prefix every name means itself, so what was checked is what is opened.
/// ffprobe accepts the prefixed form too (checked with 8.0.1).
/// </remarks>
internal static class SystemPath
{
    private const string ExtendedLengthPrefix = @"\\?\";
    private const string UncPrefix = @"\\";

    /// <summary>The path to hand to the file system and to ffprobe.</summary>
    public static string For(NormalizedPath path)
    {
        if (path.Style == PathStyle.Posix)
            return path.ToString();

        // \\server\share becomes \\?\UNC\server\share; D: becomes \\?\D:.
        var root = path.Root.StartsWith(UncPrefix, StringComparison.Ordinal)
            ? $@"{ExtendedLengthPrefix}UNC\{path.Root[UncPrefix.Length..]}"
            : ExtendedLengthPrefix + path.Root;
        return root + @"\" + string.Join('\\', path.Segments);
    }
}
