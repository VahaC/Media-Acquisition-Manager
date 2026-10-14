using System.Text;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — what a <c>torrent-add</c> adds: a .torrent read the way Transmission reads one, its files named the way the
/// version cleans names, or a magnet link without metadata. Written from Transmission's sources (4.0.6 and 4.1.3
/// <c>torrent-metainfo.cc</c>, <c>torrent-files.cc</c>, <c>magnet-metainfo.cc</c>), independently of the app's own name
/// readings, which tests check against what this gives.
/// </summary>
public sealed partial class FakeTransmissionServer
{
    // Windows' device names, which 4.0 prefixes with '_' alone or before a dot (isReservedFile).
    private static readonly string[] DeviceNames =
    [
        "AUX", "CON", "NUL", "PRN",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    // The characters 4.0 replaces with '_' (isReservedChar); '/' is split on before.
    private const string ReservedCharacters = "\"*/:<>?\\|";

    /// <summary>
    /// The torrent a <c>metainfo</c> holds, or <c>null</c> when Transmission wouldn't take it: not base64, not bencode,
    /// no v1 metadata (a BitTorrent v2-only torrent — 4.1 "missing v1 metadata", 4.0 "no files found"), no positive
    /// piece length, no file, or a path that cleans to nothing — and, on 4.1, two files cleaned to one path. Named by
    /// the SHA-1 of its info dictionary, a hybrid by its v1 hash; its <c>name</c> as the metadata gives it
    /// (<c>name.utf-8</c> first), never cleaned; its files as <see cref="ReadFiles"/> names them, none of them there yet.
    /// </summary>
    private FakeTransmissionTorrent? FromMetainfo(string base64)
    {
        if (Base64(base64) is not { } bytes || TorrentInfoHashReader.FromTorrentFile(bytes).V1 is not { } hash)
            return null;
        if (!BencodeParser.TryParse(bytes, out var root) || root!.Find("info"u8) is not { Kind: BencodeKind.Dictionary } info)
            return null;
        if (info.IntegerOf("piece length"u8) is not { } pieceLength || pieceLength <= 0)
            return null;

        var name = (info.BytesOf("name.utf-8"u8) ?? info.BytesOf("name"u8)) is { } nameBytes ? Text(nameBytes.Span) : "";
        if (ReadFiles(info, name) is not { Count: > 0 } files)
            return null;

        return new FakeTransmissionTorrent(0, hash, name.Length > 0 ? name : hash, "", 0, 0, 0, 0, 0, 0, 0, "", [], 0, 0, false, false)
        {
            PieceLength = pieceLength,
        }.WithFiles(files);
    }

    /// <summary>
    /// The torrent a magnet link (or a bare hash) names, or <c>null</c> when it names no v1 hash: without metadata, so
    /// without files and sizes, named by its <c>dn</c> percent-decoded, or by its hash without one
    /// (<c>parseMagnet</c>).
    /// </summary>
    private static FakeTransmissionTorrent? FromMagnet(string filename)
    {
        if (HashOf(filename) is not { } hash)
            return null;

        var name = MagnetName(filename.Trim(Whitespace)) is { Length: > 0 } dn ? dn : hash;
        return new FakeTransmissionTorrent(0, hash, name, "", 0, 0, 0, 0, 0, 0, 0, "", [], 0, 0, false, false);
    }

    /// <summary>A magnet link's <c>dn</c>, percent-decoded only, as <c>tr_urlPercentDecode</c> decodes it: a <c>+</c>
    /// stays a <c>+</c>.</summary>
    private static string? MagnetName(string magnet)
    {
        var query = magnet.IndexOf('?', StringComparison.Ordinal);
        if (query < 0)
            return null;

        return magnet[(query + 1)..]
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0] == "dn")
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();
    }

    /// <summary>
    /// The files of an info dictionary as the version lists them, or <c>null</c> for a torrent it refuses. A torrent of
    /// several files (<c>files</c>, even of one): each path's elements (<c>path.utf-8</c> first) cleaned and joined with
    /// <c>/</c>, under the torrent's cleaned name — under nothing when that cleans to nothing; 4.0 leaves an empty file
    /// out altogether, 4.1 lists it. A torrent of one file (<c>length</c>): that file, named by the cleaned name, directly
    /// in the folder.
    /// </summary>
    private List<FakeTransmissionFile>? ReadFiles(BencodeNode info, string name)
    {
        var portable = !Version.IsAtLeast41;
        var paths = new List<(string Path, long Length)>();
        if (info.Find("files"u8) is { Kind: BencodeKind.List } files)
        {
            foreach (var file in files.Items)
            {
                var length = file.IntegerOf("length"u8) ?? 0;
                var subpath = new StringBuilder();
                var elements = (ListOf(file, "path.utf-8"u8) ?? ListOf(file, "path"u8))?.Items ?? [];
                foreach (var element in elements.Where(element => element.Kind == BencodeKind.Bytes))
                {
                    if (subpath.Length > 0)
                        subpath.Append('/');
                    AppendCleaned(Text(element.Bytes.Span), subpath, portable);
                }

                if (portable && length == 0)
                    continue;
                // "invalid path []"
                if (subpath.Length == 0)
                    return null;
                paths.Add((subpath.ToString(), length));
            }

            // 4.1: "duplicate path [...]".
            if (!portable && paths.Select(path => path.Path).Distinct(StringComparer.Ordinal).Count() != paths.Count)
                return null;

            var folder = Cleaned(name, portable);
            if (folder.Length > 0)
                paths = [.. paths.Select(path => ($"{folder}/{path.Path}", path.Length))];
        }
        else if (info.IntegerOf("length"u8) is { } length && length != 0 && name.Length > 0)
        {
            paths.Add((Cleaned(name, portable), length));
        }

        return [.. paths.Select(path => new FakeTransmissionFile(path.Path, path.Length, 0))];
    }

    private static string Cleaned(string path, bool portable)
    {
        var cleaned = new StringBuilder();
        AppendCleaned(path, cleaned, portable);
        return cleaned.ToString();
    }

    /// <summary>
    /// 4.0's <c>makeSubpathPortable</c> or 4.1's <c>sanitize_subpath</c>, to the letter: <paramref name="path"/> split on
    /// <c>/</c> as <c>tr_strv_sep</c> splits — a piece for a leading or doubled <c>/</c>, none for a trailing one or for
    /// nothing —, each piece cleaned and followed by <c>/</c>, then the last character dropped: the <c>/</c> after the last
    /// piece, or, when the path gave none, the <c>/</c> the caller put before it, so an empty element vanishes.
    /// </summary>
    private static void AppendCleaned(string path, StringBuilder builder, bool portable)
    {
        var rest = path;
        while (rest.Length > 0)
        {
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            AppendComponent(slash < 0 ? rest : rest[..slash], builder, portable);
            builder.Append('/');
            rest = slash < 0 ? "" : rest[(slash + 1)..];
        }

        if (builder.Length > 0)
            builder.Length--;
    }

    /// <summary>
    /// One piece of a name. 4.0, on every system (<c>appendSanitizedComponent</c>): white space trimmed off both ends,
    /// trailing dots dropped, a Windows device name prefixed with <c>_</c>, and each of <c>" * : &lt; &gt; ? \ |</c>
    /// replaced with <c>_</c>. 4.1 on Linux (<c>append_sanitized_component</c>): only <c>.</c> and <c>..</c> change, to
    /// <c>_.</c> and <c>_..</c>.
    /// </summary>
    private static void AppendComponent(string piece, StringBuilder builder, bool portable)
    {
        if (!portable)
        {
            if (piece is "." or "..")
                builder.Append('_');
            builder.Append(piece);
            return;
        }

        var cleaned = piece.Trim(Whitespace).TrimEnd('.');
        if (IsDeviceName(cleaned))
            builder.Append('_');
        foreach (var character in cleaned)
            builder.Append(ReservedCharacters.Contains(character, StringComparison.Ordinal) ? '_' : character);
    }

    private static bool IsDeviceName(string piece)
    {
        var upper = piece.ToUpperInvariant();
        return DeviceNames.Any(device => upper == device || upper.StartsWith(device + ".", StringComparison.Ordinal));
    }

    /// <summary>tr_strv_convert_utf8: cut at the first NUL, anything that isn't UTF-8 replaced with U+FFFD.</summary>
    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var nul = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(nul < 0 ? bytes : bytes[..nul]);
    }

    private static BencodeNode? ListOf(BencodeNode dictionary, ReadOnlySpan<byte> key) =>
        dictionary.Find(key) is { Kind: BencodeKind.List } list ? list : null;
}
