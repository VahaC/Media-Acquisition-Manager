using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — where a client may write each of a torrent's files below a destination's folder, under every content
/// layout qBittorrent has, since the app never reads which one it is set to (ADR 0034). MAM-30 — or under the one
/// layout a client that writes the torrent's own layout has, each name read every way that client may clean it, and
/// the name an unfinished file carries meanwhile.
/// </summary>
/// <remarks>
/// <para>
/// qBittorrent 5.2's layouts (<see cref="TorrentFilePlacement.AnyOfThreeLayouts"/>): <b>Original</b> writes the
/// torrent's own paths; <b>Subfolder</b> puts a torrent of one file into a folder named after the file without its
/// extension (a torrent with a folder keeps its own); <b>NoSubfolder</b> drops a torrent's folder. The app's own
/// preference read is refused — <c>app/preferences</c> carries the Web UI's key and other passwords — so every place any
/// of them could write is a target, and a collision under any one of them counts.
/// </para>
/// <para>
/// MAM-30 — Transmission (<see cref="TorrentFilePlacement.OwnLayoutOnly"/>) has no layout setting: a torrent's own paths
/// are the only places — a torrent of one file's name straight in the folder, a torrent of several files' paths below a
/// folder named after the torrent, or straight in the folder when that name cleans to nothing (<c>finishInfoDict</c> in
/// <c>libtransmission/torrent-metainfo.cc</c>, 4.0.6 and 4.1.3). Each place is built from the names as the torrent holds
/// them (<see cref="TorrentContent.RawName"/>, <see cref="TorrentContentFile.RawPath"/>), each element cleaned on its
/// own, every way <see cref="TorrentNameReadings"/> reads it — 4.0, 4.1 on Windows, 4.1 on Linux —, as the check isn't
/// told which Transmission it is or where it runs: a target per reading that gives another place. A reading under which
/// a file's path is no name at all gives none, as that Transmission refuses the torrent ("invalid path"); a file no
/// reading places — or whose names weren't read — can't be predicted, and nor can the content. Nor can a file whose
/// path the torrent holds in bytes that aren't UTF-8 or hold a NUL (<see cref="TorrentContentFile.RawPathIsExact"/>):
/// Transmission 4.0 writes a file's path elements as the bytes they are — no U+FFFD, and a NUL cuts the whole path only
/// after cleaning, when the system reads it —, a name no path in the app says, so its place is one the check can't
/// look at.
/// </para>
/// <para>
/// MAM-30 — with a partial-file suffix (Transmission's <c>.part</c>), each file is also written under its name and the
/// suffix until it is complete, and 4.1 renames a file already there that doesn't verify to that name — so that place
/// is a target too, of the same file, which is never counted twice.
/// </para>
/// <para>
/// MAM-30 — files the torrent marks as padding (BEP 47, <see cref="TorrentContent.PadFiles"/>) are no place of
/// qBittorrent's, as libtorrent never writes one. Transmission ignores the mark (<c>attr</c> is "currently unused" in
/// 4.0.6 and 4.1.3) and writes such a file like any other, at the path the torrent gives and with the bytes its pieces
/// hash to — the zeros and the <c>.pad/N</c> name come only from the torrent's honesty —, so each is a target like a
/// real file — except one at BEP 47's own name, <c>.pad</c> then digits, which every reading keeps as it is: whatever
/// it holds, it lands in a <c>.pad</c> folder, where no library keeps a file.
/// </para>
/// </remarks>
public static class TorrentTargetPaths
{
    /// <summary>MAM-30 — every way a client that writes the torrent's own layout may clean a name, in the order its
    /// places are given.</summary>
    private static readonly Func<string, IReadOnlyList<string>>[] Readings =
        [TorrentNameReadings.Portable, TorrentNameReadings.WindowsSafe, TorrentNameReadings.Verbatim];

    /// <summary>Every place <paramref name="content"/>'s files may be written below <paramref name="folder"/> by a client
    /// that places them as <paramref name="placement"/> says, each also with <paramref name="partialFileSuffix"/> when one
    /// is given: an empty file is never one, as the client creates one only where nothing is and never shortens a
    /// file.</summary>
    /// <param name="folder">The destination's folder, inside the app.</param>
    /// <param name="content">The torrent's files.</param>
    /// <param name="placement">Where the client writes them and how it names them
    /// (<see cref="DownloadClientCapabilities.FilePlacement"/>).</param>
    /// <param name="partialFileSuffix">The suffix an unfinished file carries on disk
    /// (<see cref="DownloadClientCapabilities.PartialFileSuffix"/>); <c>null</c> or empty for none.</param>
    /// <returns>The places; MAM-30 — <c>null</c> when the client's places can't be predicted, which only
    /// <see cref="TorrentFilePlacement.OwnLayoutOnly"/> can be: as unreadable as a list of files the app can't
    /// read.</returns>
    public static IReadOnlyList<TorrentTarget>? For(
        NormalizedPath folder, TorrentContent content, TorrentFilePlacement placement, string? partialFileSuffix)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(content);
        if (placement is not (TorrentFilePlacement.AnyOfThreeLayouts or TorrentFilePlacement.OwnLayoutOnly))
            throw new ArgumentOutOfRangeException(nameof(placement), placement, "A file placement this build doesn't know.");
        if (placement == TorrentFilePlacement.OwnLayoutOnly)
            return OwnLayout(folder, content, partialFileSuffix);

        var targets = new List<TorrentTarget>();
        foreach (var file in content.Files.Where(file => file.Length > 0))
        {
            var original = folder.Append(file.Path);
            Add(targets, original, file, partialFileSuffix);

            var other = content.HasRootFolder
                ? folder.Append([.. file.Path.Skip(1)])
                : folder.Append([WithoutExtension(file.Path[0]), .. file.Path]);
            if (!other.IsSameAs(original))
                Add(targets, other, file, partialFileSuffix);
        }

        return targets;
    }

    /// <summary>
    /// MAM-30 — the places of a client that writes the torrent's own layout: for each file — the real ones, then those
    /// marked as padding, but for one at BEP 47's own name —, the place each reading gives that no reading before it
    /// gave, each followed by its suffixed place; <c>null</c> when a file has no place under any reading, its names
    /// weren't read, or its path isn't exact text.
    /// </summary>
    private static List<TorrentTarget>? OwnLayout(NormalizedPath folder, TorrentContent content, string? partialFileSuffix)
    {
        if (content.HasRootFolder && content.RawName is null)
            return null;

        // The torrent's own folder under each reading: none for a torrent of one file, and none when its name reads as
        // no name, as the client then writes the files straight into the folder.
        var ownFolders = Readings.Select(reading => content.HasRootFolder ? reading(content.RawName!) : []).ToList();
        var targets = new List<TorrentTarget>();
        var files = content.Files.Concat((content.PadFiles ?? []).Where(pad => !IsAtPaddingName(pad)));
        foreach (var file in files.Where(file => file.Length > 0))
        {
            if (file.RawPath is not { } rawPath || !file.RawPathIsExact)
                return null;

            var places = new List<NormalizedPath>();
            for (var index = 0; index < Readings.Length; index++)
            {
                var reading = Readings[index];
                var names = rawPath.SelectMany(element => reading(element)).ToList();
                if (names.Count == 0)
                    continue;

                var place = folder.Append([.. ownFolders[index], .. names]);
                if (places.Any(earlier => earlier.IsSameAs(place)))
                    continue;
                places.Add(place);
                Add(targets, place, file, partialFileSuffix);
            }

            if (places.Count == 0)
                return null;
        }

        return targets;
    }

    /// <summary>MAM-30 — whether a file marked as padding has BEP 47's own name as the torrent holds it,
    /// <c>.pad/N</c> with N digits, which no reading changes.</summary>
    private static bool IsAtPaddingName(TorrentContentFile pad) =>
        pad.RawPathIsExact && pad.RawPath is [".pad", { Length: > 0 } size] && size.All(char.IsAsciiDigit);

    /// <summary>The place, then — MAM-30 — the same place with the suffix on the file's name, for the same file.</summary>
    private static void Add(List<TorrentTarget> targets, NormalizedPath path, TorrentContentFile file, string? partialFileSuffix)
    {
        targets.Add(new TorrentTarget(path, file));
        if (!string.IsNullOrEmpty(partialFileSuffix) && path.Segments.Count > 0)
            targets.Add(new TorrentTarget(path with { Segments = [.. path.Segments.SkipLast(1), path.Segments[^1] + partialFileSuffix] }, file));
    }

    /// <summary>The name without its last extension, as qBittorrent's <c>Path::removeExtension</c> takes it: a name
    /// that starts with its only dot, or has none, stays whole.</summary>
    private static string WithoutExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
