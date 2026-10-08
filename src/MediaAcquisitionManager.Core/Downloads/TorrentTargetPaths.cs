using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — where a client may write each of a torrent's files below a destination's folder, under every content
/// layout qBittorrent has, since the app never reads which one it is set to (ADR 0034).
/// </summary>
/// <remarks>
/// qBittorrent 5.2's layouts: <b>Original</b> writes the torrent's own paths; <b>Subfolder</b> puts a torrent of one
/// file into a folder named after the file without its extension (a torrent with a folder keeps its own);
/// <b>NoSubfolder</b> drops a torrent's folder. The app's own preference read is refused — <c>app/preferences</c>
/// carries the Web UI's key and other passwords — so every place any of them could write is a target, and a
/// collision under any one of them counts.
/// </remarks>
public static class TorrentTargetPaths
{
    /// <summary>Every place <paramref name="content"/>'s files may be written below <paramref name="folder"/>: an empty
    /// file is never one, as the client creates one only where nothing is and never shortens a file.</summary>
    public static IReadOnlyList<TorrentTarget> For(NormalizedPath folder, TorrentContent content)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(content);

        var targets = new List<TorrentTarget>();
        foreach (var file in content.Files.Where(file => file.Length > 0))
        {
            var original = folder.Append(file.Path);
            targets.Add(new TorrentTarget(original, file));

            var other = content.HasRootFolder
                ? folder.Append([.. file.Path.Skip(1)])
                : folder.Append([WithoutExtension(file.Path[0]), .. file.Path]);
            if (!other.IsSameAs(original))
                targets.Add(new TorrentTarget(other, file));
        }

        return targets;
    }

    /// <summary>The name without its last extension, as qBittorrent's <c>Path::removeExtension</c> takes it: a name
    /// that starts with its only dot, or has none, stays whole.</summary>
    private static string WithoutExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
