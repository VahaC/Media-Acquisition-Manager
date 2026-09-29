using System.Diagnostics.CodeAnalysis;

namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// An absolute path split into a root and folder names, in the rules of the operating system it
/// came from (ADR 0003). The one form the app compares, stores and maps paths in.
/// </summary>
/// <remarks>
/// <para>
/// Paths are compared folder name by folder name, never as text: <c>/downloads</c> covers
/// <c>/downloads/x</c> but not <c>/downloads2/x</c>. POSIX paths compare case-sensitively (ext4:
/// <c>/Media</c> is not <c>/media</c>), Windows paths ignore case (NTFS, SMB), server and share
/// included. No Unicode normalisation: two spellings of one name stay two names, never merged by a
/// guess.
/// </para>
/// <para>
/// A <c>.</c> or <c>..</c> folder name is refused, never resolved: with a symbolic link on the way,
/// <c>..</c> doesn't lead where the text says, and a path a service reports is not trusted
/// (specification §4).
/// </para>
/// <para>
/// Equality is <see cref="IsSameAs"/> — the style's comparison of the root and every folder name —
/// rather than a record's default, which would compare the folder list by reference.
/// </para>
/// </remarks>
/// <param name="Style">The operating system's rules the path follows.</param>
/// <param name="Root"><c>/</c>, a drive (<c>D:</c>, upper case) or a UNC share
/// (<c>\\server\share</c>).</param>
/// <param name="Segments">The folder names below the root; none for the root itself.</param>
public sealed record NormalizedPath(PathStyle Style, string Root, IReadOnlyList<string> Segments)
{
    private const string PosixRoot = "/";
    private const string UncPrefix = @"\\";

    private static readonly char[] WindowsSeparators = ['\\', '/'];

    /// <summary>
    /// Reads <paramref name="text"/> as an absolute POSIX (<c>/a/b</c>) or Windows (<c>D:\a\b</c>,
    /// <c>D:/a/b</c>, <c>\\server\share\a</c>) path. Repeated and trailing separators collapse; nothing
    /// is trimmed, because a service path is taken exactly as reported. <c>//x</c> is a POSIX path,
    /// never guessed to be a UNC one.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out NormalizedPath? path, out PathParseError error)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = PathParseError.Blank;
            return false;
        }

        if (text.Any(char.IsControl))
        {
            error = PathParseError.ControlCharacter;
            return false;
        }

        PathStyle style;
        string root;
        string[] segments;
        if (text.StartsWith(UncPrefix, StringComparison.Ordinal))
        {
            var parts = text[UncPrefix.Length..].Split(WindowsSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                error = PathParseError.NotAbsolute;
                return false;
            }

            style = PathStyle.Windows;
            root = $@"{UncPrefix}{parts[0]}\{parts[1]}";
            if (IsDotSegment(parts[0]) || IsDotSegment(parts[1]))
            {
                error = PathParseError.DotSegment;
                return false;
            }

            segments = parts[2..];
        }
        else if (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/')
        {
            style = PathStyle.Windows;
            root = $"{char.ToUpperInvariant(text[0])}:";
            segments = text[3..].Split(WindowsSeparators, StringSplitOptions.RemoveEmptyEntries);
        }
        else if (text[0] == '/')
        {
            style = PathStyle.Posix;
            root = PosixRoot;
            segments = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            // Relative, home-relative (~/x), drive-relative (C:x): each means something different
            // depending on where the reader stands, so none can be mapped.
            error = PathParseError.NotAbsolute;
            return false;
        }

        if (segments.Any(IsDotSegment))
        {
            error = PathParseError.DotSegment;
            return false;
        }

        path = new NormalizedPath(style, root, Array.AsReadOnly(segments));
        error = PathParseError.None;
        return true;
    }

    /// <summary>
    /// Whether this path is <paramref name="prefix"/> or inside it, folder name by folder name. A path
    /// of another style is never under it.
    /// </summary>
    public bool IsUnder(NormalizedPath prefix)
    {
        if (Style != prefix.Style || prefix.Segments.Count > Segments.Count)
            return false;

        var comparer = Comparer;
        if (!comparer.Equals(Root, prefix.Root))
            return false;

        for (var index = 0; index < prefix.Segments.Count; index++)
        {
            if (!comparer.Equals(Segments[index], prefix.Segments[index]))
                return false;
        }

        return true;
    }

    /// <summary>The folder names below <paramref name="prefix"/>; none when this path is the prefix
    /// itself. Only for a path <see cref="IsUnder"/> the prefix.</summary>
    public IReadOnlyList<string> SegmentsAfter(NormalizedPath prefix)
    {
        if (!IsUnder(prefix))
            throw new ArgumentException($"“{this}” is not under “{prefix}”.", nameof(prefix));

        return [.. Segments.Skip(prefix.Segments.Count)];
    }

    /// <summary>This path with <paramref name="segments"/> below it, in this path's style.</summary>
    public NormalizedPath Append(IReadOnlyList<string> segments) => this with { Segments = [.. Segments, .. segments] };

    /// <summary>Whether both name the same place under the style's rules: letter case counts for
    /// POSIX paths and not for Windows ones.</summary>
    public bool IsSameAs(NormalizedPath other) =>
        Style == other.Style && Segments.Count == other.Segments.Count && IsUnder(other);

    /// <inheritdoc cref="IsSameAs"/>
    public bool Equals(NormalizedPath? other) => other is not null && IsSameAs(other);

    /// <summary>A hash that agrees with <see cref="Equals(NormalizedPath?)"/>.</summary>
    public override int GetHashCode()
    {
        var comparer = Comparer;
        var hash = new HashCode();
        hash.Add(Style);
        hash.Add(Root, comparer);
        foreach (var segment in Segments)
            hash.Add(segment, comparer);
        return hash.ToHashCode();
    }

    /// <summary>The canonical form, which is what gets stored and echoed: <c>/a/b</c>, <c>/</c>,
    /// <c>D:\a\b</c>, <c>D:\</c>, <c>\\server\share\a</c>.</summary>
    public override string ToString() => Style switch
    {
        PathStyle.Posix => PosixRoot + string.Join('/', Segments),
        _ when Root.StartsWith(UncPrefix, StringComparison.Ordinal) && Segments.Count == 0 => Root,
        _ => Root + @"\" + string.Join('\\', Segments),
    };

    private StringComparer Comparer => Style == PathStyle.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsDotSegment(string segment) => segment is "." or "..";
}
