using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Works out where a path really leads (MAM-9): every link on the way followed, folder name by folder name
/// from the root, into a path with no link left in it.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0009 — a path's text says nothing about where it leads: <c>/media/movies/x.mkv</c> can be a symbolic
/// link to <c>/etc/shadow</c>, and so can any folder on the way. Asking the operating system for the final
/// path would follow the links and then only tell where they ended; walking them here lets the inventory
/// compare the place the file really is with the allowed roots (specification §4) and open that place, so
/// neither the app nor ffprobe follows a link to get there.
/// </para>
/// <para>
/// A link is anything <see cref="FileSystemInfo.LinkTarget"/> reports: a symbolic link, or on Windows also a
/// junction. A relative target is read against the link's folder, an absolute one starts over at its root,
/// and a <c>..</c> in a target steps back over the folders already resolved — which hold no link any more,
/// so <c>..</c> means what it says there, unlike in the text of a path that has links in it.
/// </para>
/// <para>
/// Only metadata is read here: whether a name exists, is a folder, or is a link and to where. Nothing is
/// opened, so a link leading outside the roots is looked at, never read through.
/// </para>
/// </remarks>
internal static class LinkResolver
{
    /// <summary>
    /// Linux's own limit (<c>MAXSYMLINKS</c>): a path needing more is refused with <c>ELOOP</c> by the kernel
    /// too, so no real library needs more, and a loop of links ends here instead of running forever.
    /// </summary>
    public const int MaximumLinkHops = 40;

    private const string ParentFolder = "..";
    private const string CurrentFolder = ".";
    private const string UncPrefix = @"\\";

    private static readonly char[] WindowsSeparators = ['\\', '/'];

    /// <summary>
    /// <paramref name="path"/> with every link on the way followed: <see cref="FileInventoryStatus.Found"/> and the
    /// link-free path, or why it couldn't be followed — <see cref="FileInventoryStatus.NotFound"/> for a missing
    /// name, <see cref="FileInventoryStatus.Unreadable"/> for more than <see cref="MaximumLinkHops"/> links or a
    /// folder the app may not look into, <see cref="FileInventoryStatus.OutsideAllowedRoots"/> for a link whose
    /// target can't be followed safely. The last name may be anything (a file, a folder, a device); every
    /// name before it must be a folder.
    /// </summary>
    /// <param name="path">An absolute path of this platform's style.</param>
    /// <param name="appPath">The path as the caller gave it, for the sentences.</param>
    public static FileInventoryResult<NormalizedPath> Resolve(NormalizedPath path, string appPath) =>
        Resolve(path, appPath, out _);

    /// <inheritdoc cref="Resolve(NormalizedPath, string)"/>
    /// <param name="path">An absolute path of this platform's style.</param>
    /// <param name="appPath">The path as the caller gave it, for the sentences.</param>
    /// <param name="refused">
    /// MAM-18 — <c>true</c> when the resolver itself refused to follow the links (more than
    /// <see cref="MaximumLinkHops"/> of them, or a target it can't follow safely); <c>false</c> when the file system
    /// couldn't answer now (a name missing, a folder the app may not look into, an I/O error), which the walk of
    /// the media folders reads as a place it couldn't list rather than one it refuses (ADR 0021).
    /// </param>
    public static FileInventoryResult<NormalizedPath> Resolve(NormalizedPath path, string appPath, out bool refused)
    {
        refused = false;
        var root = path.Root;
        var resolved = new List<string>(path.Segments.Count);
        var pending = new Stack<string>(path.Segments.Reverse());
        var hops = 0;
        while (pending.TryPop(out var name))
        {
            // Only a link's target brings these: a path the caller gives has neither (NormalizedPath).
            if (name is "" or CurrentFolder)
                continue;

            if (name == ParentFolder)
            {
                // At the root, .. stays at the root, as the kernel has it.
                if (resolved.Count > 0)
                    resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            var candidate = new FileInfo(SystemPath.For(new NormalizedPath(path.Style, root, [.. resolved, name])));
            string? target;
            FileAttributes attributes;
            try
            {
                target = candidate.LinkTarget;
                attributes = target is null ? candidate.Attributes : default;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return FileSystemFailure.For<NormalizedPath>(exception, appPath);
            }

            if (target is not null)
            {
                if (++hops > MaximumLinkHops)
                {
                    refused = true;
                    return FileSystemFailure.Refused<NormalizedPath>(FileInventoryStatus.Unreadable, FileInventoryMessages.TooManyLinks(appPath));
                }

                if (!TrySplitTarget(target, path.Style, root, out var targetRoot, out var targetSegments))
                {
                    refused = true;
                    return FileSystemFailure.Refused<NormalizedPath>(
                        FileInventoryStatus.OutsideAllowedRoots, FileInventoryMessages.UnfollowableLink(appPath, target));
                }

                if (targetRoot is not null)
                {
                    root = targetRoot;
                    resolved.Clear();
                }

                // The target's names come before the rest of the path.
                for (var index = targetSegments.Length - 1; index >= 0; index--)
                    pending.Push(targetSegments[index]);
                continue;
            }

            // -1: nothing by that name (FileSystemInfo's way of saying so without throwing).
            if ((int)attributes == -1)
                return FileSystemFailure.Refused<NormalizedPath>(FileInventoryStatus.NotFound, FileInventoryMessages.NotFound(appPath));

            // A name below something that isn't a folder doesn't exist (ENOTDIR).
            if (pending.Count > 0 && !attributes.HasFlag(FileAttributes.Directory))
                return FileSystemFailure.Refused<NormalizedPath>(FileInventoryStatus.NotFound, FileInventoryMessages.NotFound(appPath));

            resolved.Add(name);
        }

        return new FileInventoryResult<NormalizedPath>(
            FileInventoryStatus.Found, new NormalizedPath(path.Style, root, resolved.AsReadOnly()), null);
    }

    /// <summary>
    /// Splits a link's target into the root it starts over at (<c>null</c> when it is relative to the link's
    /// folder) and its names, <c>.</c> and <c>..</c> kept. False for a target that can't be followed safely: an
    /// empty one, one with control characters, a Windows target relative to another drive's current folder
    /// (<c>D:x</c>), or one that names a device or a volume rather than a folder (<c>\\.\</c>,
    /// <c>\\?\Volume{…}</c>).
    /// </summary>
    private static bool TrySplitTarget(string target, PathStyle style, string currentRoot, out string? root, out string[] segments)
    {
        root = null;
        segments = [];
        if (target.Length == 0 || target.Any(char.IsControl))
            return false;

        if (style == PathStyle.Posix)
        {
            root = target[0] == '/' ? "/" : null;
            segments = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return true;
        }

        var text = target;
        // How a junction or a symbolic link may store an absolute target: NT (\??\D:\x) or extended-length
        // (\\?\D:\x, \\?\UNC\server\share). After the prefix only a drive or a share is a folder.
        if (text.StartsWith(@"\??\", StringComparison.Ordinal) || text.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            text = text[4..];
            if (text.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
                text = UncPrefix + text[4..];
            else if (!IsDriveAbsolute(text))
                return false;
        }

        if (text.StartsWith(UncPrefix, StringComparison.Ordinal))
        {
            var parts = text[UncPrefix.Length..].Split(WindowsSeparators, StringSplitOptions.RemoveEmptyEntries);
            // \\.\ and \\?\ name devices and volumes, not shares.
            if (parts.Length < 2 || parts[0] is CurrentFolder or ParentFolder or "?" || parts[1] is CurrentFolder or ParentFolder)
                return false;

            root = $@"{UncPrefix}{parts[0]}\{parts[1]}";
            segments = parts[2..];
            return true;
        }

        if (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':')
        {
            if (!IsDriveAbsolute(text))
                return false;

            root = $"{char.ToUpperInvariant(text[0])}:";
            segments = text[3..].Split(WindowsSeparators, StringSplitOptions.RemoveEmptyEntries);
            return true;
        }

        // \x: the root of the link's own drive or share.
        if (text[0] is '\\' or '/')
            root = currentRoot;

        segments = text.Split(WindowsSeparators, StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    private static bool IsDriveAbsolute(string text) =>
        text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/';
}
