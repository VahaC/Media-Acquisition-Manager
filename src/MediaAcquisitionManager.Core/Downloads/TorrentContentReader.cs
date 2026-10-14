using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;
using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — reads which files a .torrent will write, how long each is and the torrent's hashes of a few of its
/// pieces, for the check that a download lands on no different file already there (ADR 0034).
/// </summary>
/// <remarks>
/// <para>
/// v1 metadata — <c>name</c>, <c>length</c> or <c>files</c>, and <c>pieces</c> (BEP 3), <c>name.utf-8</c> and
/// <c>path.utf-8</c> before the plain ones as libtorrent prefers them, pad files (<c>attr</c> with <c>p</c>) left
/// out as files but counted where the pieces lie (BEP 47) — is read whenever the torrent has it, a hybrid's
/// included: its paths are the v2 file tree's, which libtorrent requires to agree. A torrent with v2 metadata alone
/// is read from its <c>file tree</c> and its <c>piece layers</c> (BEP 52).
/// </para>
/// <para>
/// Every name is cleaned as libtorrent 2.0 cleans it (<see cref="TorrentPathElement"/>, <c>src/torrent_info.cpp</c>):
/// a torrent's folder is its name, <c>_</c> when the name is empty and the v1 info hash when cleaning drops it; each
/// element of a file's path, and each key of a file tree, is always one element. A torrent of one file whose name
/// is empty or dropped is one libtorrent refuses, and so is no content here.
/// </para>
/// <para>
/// MAM-30 — beside them, the torrent's name and each file's elements as the torrent holds them
/// (<see cref="TorrentContent.RawName"/>, <see cref="TorrentContentFile.RawPath"/>), for a client that cleans names
/// its own way. They are read as Transmission reads text (<c>tr_strv_convert_utf8</c>): cut at the first NUL, then each
/// sequence that isn't valid UTF-8 replaced as utf8cpp's <c>replace_invalid</c> replaces it — one U+FFFD for a lead
/// byte that starts none, one for an overlong form, a surrogate or a code point past U+10FFFF with the continuation
/// bytes after it, one for a sequence the text ends inside —, so a name holds the U+FFFD Transmission 4.1 writes.
/// Transmission 4.0 reads the name so too, but writes a file's path elements as the bytes they are, cleaned before the
/// system cuts them at a NUL (<c>torrent-metainfo.cc</c>, 4.0.6), so a file whose elements aren't valid UTF-8 or hold
/// a NUL is marked (<see cref="TorrentContentFile.RawPathIsExact"/>): no text names where 4.0 writes it. A hybrid's
/// are its v1 name and files, as Transmission ignores the file tree ("'file tree' is ignored", 4.0.6 and 4.1.3).
/// </para>
/// <para>
/// MAM-30 — a file marked as padding that has a path is kept apart (<see cref="TorrentContent.PadFiles"/>), read as
/// a real file is: libtorrent never writes one, but Transmission ignores the mark (<c>attr</c> is "currently unused" in
/// 4.0.6 and 4.1.3) and writes it at its path like any other file. One without a path Transmission never writes
/// either: it refuses such a torrent ("invalid path"), or 4.0 skips an empty one.
/// </para>
/// <para>
/// Defensive, like <see cref="TorrentInfoHashReader"/>: anything that isn't a torrent the app can read — no info
/// dictionary, a piece length that isn't positive or is longer than libtorrent takes, pieces that don't add up to
/// the files, no name — gives <c>null</c>, never a guess or an exception. So does a torrent libtorrent refuses that
/// another client might take (a longer piece, a torrent of one file whose name libtorrent drops): the check then
/// can't read its files, whatever the client.
/// </para>
/// </remarks>
public static class TorrentContentReader
{
    /// <summary>
    /// How many pieces of one file are compared: the first, one in the middle and the last. Enough to tell another
    /// release from the same bytes — a re-encode or another edition differs in every piece — at the cost of three
    /// pieces read from the disk, not a whole file.
    /// </summary>
    private const int MaximumSamplesPerFile = 3;

    private const int Sha1Length = 20;

    private const int Sha256Length = 32;

    /// <summary>MAM-30 — what Transmission writes for a sequence that isn't valid UTF-8 (U+FFFD).</summary>
    private const char ReplacementCharacter = '\uFFFD';

    /// <summary>
    /// libtorrent's longest piece (<c>file_storage::max_piece_size</c>, 32,767 blocks of 16 KiB): it refuses a
    /// torrent with a longer one, so qBittorrent never writes it.
    /// </summary>
    private const long MaximumPieceLength = 32_767L * TorrentPieceHash.BlockSize;

    /// <summary>The files of <paramref name="torrent"/>, or <c>null</c> when it isn't a .torrent the app can read.</summary>
    public static TorrentContent? Read(ReadOnlyMemory<byte> torrent)
    {
        if (!BencodeParser.TryParse(torrent, out var root) || root!.Kind != BencodeKind.Dictionary)
            return null;
        if (root.Find("info"u8) is not { Kind: BencodeKind.Dictionary } info)
            return null;
        if (info.IntegerOf("piece length"u8) is not { } pieceLength || pieceLength is <= 0 or > MaximumPieceLength)
            return null;
        // libtorrent prefers name.utf-8, and refuses a torrent with neither.
        if ((info.BytesOf("name.utf-8"u8) ?? info.BytesOf("name"u8)) is not { } name)
            return null;

        if (info.BytesOf("pieces"u8) is { } pieces)
            return ReadV1(info, name, pieceLength, pieces);
        if (info.IntegerOf("meta version"u8) == 2)
            return ReadV2(root, info, name, pieceLength);
        return null;
    }

    /// <summary>
    /// The folder of a torrent with one, as libtorrent names it: its name cleaned (<c>_</c> when empty), or the
    /// hexadecimal v1 info hash when cleaning drops the name — the SHA-1 of the info dictionary, a v2 torrent's
    /// too, as libtorrent works the folder out before it clears a v2 torrent's v1 hash.
    /// </summary>
    private static string FolderOf(BencodeNode info, ReadOnlyMemory<byte> name) =>
        TorrentPathElement.Sanitize(name.Span, force: false) ?? Convert.ToHexStringLower(SHA1.HashData(info.Encoded.Span));

    // ── v1 ────────────────────────────────────────────────────────────────────────────────────────

    private static TorrentContent? ReadV1(BencodeNode info, ReadOnlyMemory<byte> name, long pieceLength, ReadOnlyMemory<byte> pieces)
    {
        if (pieces.Length % Sha1Length != 0)
            return null;

        var entries = new List<(IReadOnlyList<string>? Path, IReadOnlyList<string>? RawPath, bool RawPathIsExact, long Length, bool IsPad)>();
        bool hasRootFolder;
        if (info.Find("files"u8) is { } files)
        {
            if (files.Kind != BencodeKind.List || files.Items.Count == 0)
                return null;
            hasRootFolder = true;
            var folder = FolderOf(info, name);
            foreach (var file in files.Items)
            {
                if (file.IntegerOf("length"u8) is not { } length || length < 0)
                    return null;

                var isPad = file.BytesOf("attr"u8) is { } attributes && attributes.Span.IndexOf((byte)'p') >= 0;
                // As libtorrent finds it: path.utf-8 when it is a list, else path. A pad file needs none, as the
                // client never writes one; any other file without one is a torrent libtorrent refuses.
                if ((ListOf(file, "path.utf-8"u8) ?? ListOf(file, "path"u8)) is not { Items.Count: > 0 } path)
                {
                    if (!isPad)
                        return null;
                    entries.Add((null, null, true, length, true));
                    continue;
                }

                if (PathOf(folder, path) is not { } elements)
                    return null;
                // MAM-30 — Transmission prefers path.utf-8 too: it drops a path read before it.
                entries.Add((
                    elements,
                    [.. path.Items.Select(element => TextOf(element.Bytes.Span))],
                    path.Items.All(element => IsExactText(element.Bytes.Span)),
                    length,
                    isPad));
            }
        }
        else if (info.IntegerOf("length"u8) is { } length && length >= 0)
        {
            // A torrent of one file is that file, named by the torrent's name, which libtorrent requires: an empty
            // one, or one cleaning drops, makes a torrent it refuses — never a file named by the info hash.
            if (name.IsEmpty || TorrentPathElement.Sanitize(name.Span, force: false) is not { } fileName)
                return null;
            hasRootFolder = false;
            entries.Add(([fileName], [TextOf(name.Span)], true, length, false));
        }
        else
        {
            return null;
        }

        long total = 0;
        foreach (var entry in entries)
        {
            if (total > long.MaxValue - entry.Length)
                return null;
            total += entry.Length;
        }

        var pieceCount = pieces.Length / Sha1Length;
        if (pieceCount != (total == 0 ? 0 : (total - 1) / pieceLength + 1))
            return null;

        var read = new List<TorrentContentFile>();
        var pads = new List<TorrentContentFile>();
        long offset = 0;
        foreach (var (path, rawPath, rawPathIsExact, length, isPad) in entries)
        {
            // MAM-30 — a pad file with a path is kept apart, for a client that ignores the mark.
            if (path is not null)
                (isPad ? pads : read).Add(new TorrentContentFile(path, length, V1Samples(offset, offset + length, pieceLength, total, pieces), rawPath, rawPathIsExact));
            offset += length;
        }

        return new TorrentContent(hasRootFolder, read, TextOf(name.Span), pads);
    }

    /// <summary>The torrent's folder, then each element of <paramref name="path"/> cleaned as libtorrent forces
    /// it, so none is dropped; <c>null</c> when one isn't a string, which makes a torrent libtorrent refuses.</summary>
    private static List<string>? PathOf(string folder, BencodeNode path)
    {
        var segments = new List<string> { folder };
        foreach (var element in path.Items)
        {
            if (element.Kind != BencodeKind.Bytes)
                return null;
            segments.Add(TorrentPathElement.Sanitize(element.Bytes.Span, force: true)!);
        }

        return segments;
    }

    /// <summary>The value under <paramref name="key"/> when it is a list, as libtorrent's <c>dict_find_list</c>
    /// finds one.</summary>
    private static BencodeNode? ListOf(BencodeNode dictionary, ReadOnlySpan<byte> key) =>
        dictionary.Find(key) is { Kind: BencodeKind.List } list ? list : null;

    /// <summary>
    /// Up to <see cref="MaximumSamplesPerFile"/> of the pieces that lie wholly inside the file from
    /// <paramref name="start"/> to <paramref name="end"/> of the torrent's bytes: a v1 piece can span files, and
    /// one that does says nothing of either file alone.
    /// </summary>
    private static List<TorrentPieceSample> V1Samples(long start, long end, long pieceLength, long total, ReadOnlyMemory<byte> pieces)
    {
        if (end <= start)
            return [];

        var first = (start + pieceLength - 1) / pieceLength;
        var lastPiece = (total - 1) / pieceLength;
        // A piece ends at the next piece's start, or the last one at the torrent's end.
        var last = end == total ? lastPiece : end / pieceLength - 1;
        if (last < first)
            return [];

        return
        [
            .. Indices(first, last).Select(index =>
            {
                var pieceStart = index * pieceLength;
                var pieceEnd = Math.Min(pieceStart + pieceLength, total);
                return new TorrentPieceSample(
                    new FilePieceRange(pieceStart - start, (int)(pieceEnd - pieceStart), FilePieceHashing.Sha1, 0),
                    pieces.Slice((int)index * Sha1Length, Sha1Length).ToArray());
            }),
        ];
    }

    // ── v2 ────────────────────────────────────────────────────────────────────────────────────────

    private static TorrentContent? ReadV2(BencodeNode root, BencodeNode info, ReadOnlyMemory<byte> name, long pieceLength)
    {
        // BEP 52: a power of two, at least one 16 KiB block.
        if (pieceLength < TorrentPieceHash.BlockSize || !BitOperations.IsPow2(pieceLength))
            return null;
        if (info.Find("file tree"u8) is not { Kind: BencodeKind.Dictionary } tree || tree.Keys.Count == 0)
            return null;

        // As libtorrent's extract_files2 tells it: one file alone at the top of the tree, in a torrent with no v1
        // list of files, is a torrent of one file with no folder of its own; anything else lies under the folder.
        var hasRootFolder = !(info.Find("files"u8) is null && tree.Keys.Count == 1 && IsLeaf(tree.Items[0]));
        var leaves = new List<(List<string> Elements, List<string> RawElements, bool RawElementsAreExact, BencodeNode Leaf)>();
        if (!TryWalk(tree, hasRootFolder ? [FolderOf(info, name)] : [], [], rawElementsAreExact: true, leaves, depth: 1))
            return null;

        var layers = root.Find("piece layers"u8);
        var read = new List<TorrentContentFile>();
        foreach (var (elements, rawElements, rawElementsAreExact, leaf) in leaves)
        {
            if (leaf.IntegerOf("length"u8) is not { } length || length < 0)
                return null;

            var samples = new List<TorrentPieceSample>();
            if (length > 0)
            {
                if (leaf.BytesOf("pieces root"u8) is not { Length: Sha256Length } piecesRoot)
                    return null;
                samples = V2Samples(length, pieceLength, piecesRoot, layers);
            }

            read.Add(new TorrentContentFile(elements, length, samples, rawElements, rawElementsAreExact));
        }

        return new TorrentContent(hasRootFolder, read, TextOf(name.Span));
    }

    /// <summary>
    /// The files of a v2 file tree, as libtorrent's <c>extract_files2</c> walks it: each folder a dictionary of
    /// names, none of them empty; each file a dictionary whose one key is empty. Every name is cleaned as libtorrent
    /// forces it, so none is dropped. <paramref name="elements"/> is the path so far: the torrent's folder at the
    /// top, or nothing for a torrent of one file; <paramref name="rawElements"/> the same path below the torrent's
    /// folder as the tree holds it, and <paramref name="rawElementsAreExact"/> whether each of its keys is valid UTF-8
    /// with no NUL (MAM-30).
    /// </summary>
    private static bool TryWalk(
        BencodeNode folder,
        List<string> elements,
        List<string> rawElements,
        bool rawElementsAreExact,
        List<(List<string>, List<string>, bool, BencodeNode)> leaves,
        int depth)
    {
        if (depth > 100)
            return false;
        for (var index = 0; index < folder.Keys.Count; index++)
        {
            var child = folder.Items[index];
            if (child.Kind != BencodeKind.Dictionary || folder.Keys[index].IsEmpty)
                return false;

            List<string> childElements = [.. elements, TorrentPathElement.Sanitize(folder.Keys[index].Span, force: true)!];
            List<string> childRawElements = [.. rawElements, TextOf(folder.Keys[index].Span)];
            var childRawElementsAreExact = rawElementsAreExact && IsExactText(folder.Keys[index].Span);
            if (IsLeaf(child))
            {
                if (child.Items[0].Kind != BencodeKind.Dictionary)
                    return false;
                leaves.Add((childElements, childRawElements, childRawElementsAreExact, child.Items[0]));
            }
            else if (!TryWalk(child, childElements, childRawElements, childRawElementsAreExact, leaves, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A file of a v2 file tree: a dictionary whose one key is empty.</summary>
    private static bool IsLeaf(BencodeNode node) =>
        node.Kind == BencodeKind.Dictionary && node.Keys.Count == 1 && node.Keys[0].IsEmpty;

    /// <summary>
    /// A v2 file's pieces to compare: a file no longer than a piece is one tree, its root the file's pieces root;
    /// a longer one is compared by its piece layer, every piece of it inside the file (BEP 52 aligns them). A file
    /// whose layer is missing or the wrong length is compared by its size alone.
    /// </summary>
    private static List<TorrentPieceSample> V2Samples(long length, long pieceLength, ReadOnlyMemory<byte> piecesRoot, BencodeNode? layers)
    {
        if (length <= pieceLength)
        {
            var blocks = (int)((length - 1) / TorrentPieceHash.BlockSize + 1);
            return [new TorrentPieceSample(
                new FilePieceRange(0, (int)length, FilePieceHashing.Sha256Tree, (int)BitOperations.RoundUpToPowerOf2((uint)blocks)),
                piecesRoot.ToArray())];
        }

        var count = (length - 1) / pieceLength + 1;
        if (layers?.BytesOf(piecesRoot.Span) is not { } layer || layer.Length != count * Sha256Length)
            return [];

        var leafCount = (int)(pieceLength / TorrentPieceHash.BlockSize);
        return
        [
            .. Indices(0, count - 1).Select(index => new TorrentPieceSample(
                new FilePieceRange(index * pieceLength, (int)Math.Min(pieceLength, length - index * pieceLength), FilePieceHashing.Sha256Tree, leafCount),
                layer.Slice((int)index * Sha256Length, Sha256Length).ToArray())),
        ];
    }

    /// <summary>The first, middle and last of <paramref name="first"/> to <paramref name="last"/>, each once.</summary>
    private static IEnumerable<long> Indices(long first, long last) =>
        new[] { first, first + (last - first) / 2, last }.Distinct().Take(MaximumSamplesPerFile);

    // ── text ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MAM-30 — <paramref name="raw"/> as Transmission reads a name (<c>tr_strv_replace_invalid</c>): cut at the first
    /// NUL, then read as utf8cpp's <c>replace_invalid</c> reads it — each valid sequence kept; one U+FFFD for a lead byte
    /// that starts none; one for a sequence the text ends inside, which ends the text; and one for any other sequence
    /// that isn't valid — cut short, overlong, a surrogate, past U+10FFFF — together with every continuation byte after
    /// its lead byte. .NET's own decoder marks some of those byte by byte, which would name another file.
    /// </summary>
    private static string TextOf(ReadOnlySpan<byte> raw)
    {
        var end = raw.IndexOf((byte)0);
        var bytes = end < 0 ? raw : raw[..end];

        var text = new StringBuilder(bytes.Length);
        for (var index = 0; index < bytes.Length;)
        {
            var lead = bytes[index];
            var length = lead < 0b1000_0000 ? 1
                : lead >> 5 == 0b110 ? 2
                : lead >> 4 == 0b1110 ? 3
                : lead >> 3 == 0b1_1110 ? 4
                : 0;
            if (length == 0)
            {
                text.Append(ReplacementCharacter);
                index++;
                continue;
            }

            var codePoint = length switch { 1 => lead, 2 => lead & 0b1_1111, 3 => lead & 0b1111, _ => lead & 0b111 };
            var read = 1;
            while (read < length && index + read < bytes.Length && IsContinuation(bytes[index + read]))
                codePoint = (codePoint << 6) | (bytes[index + read++] & 0b11_1111);

            if (read < length && index + read == bytes.Length)
            {
                text.Append(ReplacementCharacter);
                break;
            }

            if (read < length || IsOverlong(codePoint, length) || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
            {
                text.Append(ReplacementCharacter);
                index++;
                while (index < bytes.Length && IsContinuation(bytes[index]))
                    index++;
                continue;
            }

            text.Append(char.ConvertFromUtf32(codePoint));
            index += length;
        }

        return text.ToString();
    }

    /// <summary>
    /// MAM-30 — whether <paramref name="raw"/> is the text <see cref="TextOf"/> reads exactly: valid UTF-8 (no overlong
    /// form, surrogate or code point past U+10FFFF, as utf8cpp checks it) with no NUL, so a client that writes the bytes
    /// as they are writes that text.
    /// </summary>
    private static bool IsExactText(ReadOnlySpan<byte> raw) => raw.IndexOf((byte)0) < 0 && Utf8.IsValid(raw);

    /// <summary>A byte that continues a UTF-8 sequence: <c>10xxxxxx</c>.</summary>
    private static bool IsContinuation(byte value) => value >> 6 == 0b10;

    /// <summary>A code point written with more bytes than it needs, which UTF-8 forbids.</summary>
    private static bool IsOverlong(int codePoint, int length) =>
        codePoint < 0x80 ? length != 1
        : codePoint < 0x800 ? length != 2
        : codePoint < 0x10000 && length != 3;
}
