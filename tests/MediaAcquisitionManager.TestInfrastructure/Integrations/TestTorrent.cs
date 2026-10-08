using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-38 — real .torrent files built from the bytes a test plants, for the overwrite check: v1 with SHA-1 pieces
/// (BEP 3), optionally with pad files (BEP 47), and v2 with its file tree and piece layers (BEP 52). The hashes are
/// worked out here, independently of the app's code that checks them.
/// </summary>
public static class TestTorrent
{
    /// <summary>The smallest piece BitTorrent has: one 16 KiB block.</summary>
    public const int PieceLength = 16 * 1024;

    private const int BlockSize = 16 * 1024;

    /// <summary><paramref name="length"/> bytes that differ with <paramref name="seed"/>, for a file's content.</summary>
    public static byte[] Bytes(int length, byte seed)
    {
        var bytes = new byte[length];
        for (var index = 0; index < length; index++)
            bytes[index] = (byte)((index * 31) + seed + (index >> 8));
        return bytes;
    }

    /// <summary>A v1 torrent of one file, <paramref name="name"/>.</summary>
    public static byte[] V1(string name, byte[] content, int pieceLength = PieceLength) =>
        Encode(Dictionary(("info", Dictionary(
            ("length", (long)content.Length),
            ("name", name),
            ("piece length", (long)pieceLength),
            ("pieces", Pieces(content, pieceLength))))));

    /// <summary>
    /// A v1 torrent of several files under a folder <paramref name="name"/>, each <c>Path</c> below it; with
    /// <paramref name="padFiles"/>, a pad file after each but the last fills its last piece, as BEP 47 aligns them.
    /// </summary>
    public static byte[] V1(string name, IReadOnlyList<(string[] Path, byte[] Content)> files, int pieceLength = PieceLength, bool padFiles = false)
    {
        var entries = new List<object>();
        var data = new List<byte>();
        for (var index = 0; index < files.Count; index++)
        {
            var (path, content) = files[index];
            entries.Add(Dictionary(("length", (long)content.Length), ("path", path.Cast<object>().ToList())));
            data.AddRange(content);
            var gap = (pieceLength - (content.Length % pieceLength)) % pieceLength;
            if (padFiles && index < files.Count - 1 && gap > 0)
            {
                entries.Add(Dictionary(("attr", "p"), ("length", (long)gap), ("path", new List<object> { ".pad", gap.ToString(CultureInfo.InvariantCulture) })));
                data.AddRange(new byte[gap]);
            }
        }

        return Encode(Dictionary(("info", Dictionary(
            ("files", entries),
            ("name", name),
            ("piece length", (long)pieceLength),
            ("pieces", Pieces([.. data], pieceLength))))));
    }

    /// <summary>
    /// A v2 torrent (no v1 metadata): one file named <paramref name="name"/> when <paramref name="files"/> holds one
    /// whose path is that name, else the files under a folder <paramref name="name"/>.
    /// </summary>
    public static byte[] V2(string name, IReadOnlyList<(string[] Path, byte[] Content)> files, int pieceLength = PieceLength)
    {
        var tree = new SortedDictionary<string, object>(StringComparer.Ordinal);
        var layers = new List<(object Key, object Value)>();
        foreach (var (path, content) in files)
        {
            var folder = tree;
            foreach (var element in path[..^1])
            {
                if (!folder.TryGetValue(element, out var child))
                    folder[element] = child = new SortedDictionary<string, object>(StringComparer.Ordinal);
                folder = (SortedDictionary<string, object>)child;
            }

            var leaf = new List<(object, object)> { ("length", (long)content.Length) };
            if (content.Length > 0)
            {
                var root = FileRoot(content);
                leaf.Add(("pieces root", root));
                if (content.Length > pieceLength)
                    layers.Add((root, PieceLayer(content, pieceLength)));
            }

            folder[path[^1]] = Dictionary(("", Dictionary([.. leaf])));
        }

        return Encode(Dictionary(
            ("info", Dictionary(("file tree", tree), ("meta version", 2L), ("name", name), ("piece length", (long)pieceLength))),
            ("piece layers", Dictionary([.. layers]))));
    }

    /// <summary>A dictionary of <paramref name="entries"/>, its keys strings or bytes, encoded in their byte order.</summary>
    public static List<(object Key, object Value)> Dictionary(params (object Key, object Value)[] entries) => [.. entries];

    /// <summary>Bencode of <paramref name="value"/>: a <see cref="long"/>, a <see cref="string"/> (UTF-8),
    /// <see cref="byte"/>s, a list, a <see cref="Dictionary"/> or a sorted dictionary of strings.</summary>
    public static byte[] Encode(object value)
    {
        using var stream = new MemoryStream();
        Write(stream, value);
        return stream.ToArray();
    }

    private static void Write(MemoryStream stream, object value)
    {
        switch (value)
        {
            case long integer:
                WriteAscii(stream, $"i{integer.ToString(CultureInfo.InvariantCulture)}e");
                break;
            case string text:
                WriteBytes(stream, Encoding.UTF8.GetBytes(text));
                break;
            case byte[] bytes:
                WriteBytes(stream, bytes);
                break;
            case List<(object Key, object Value)> entries:
                stream.WriteByte((byte)'d');
                foreach (var (key, item) in entries.Select(entry => (Key: KeyBytes(entry.Key), entry.Value)).OrderBy(entry => entry.Key, ByteOrder.Instance))
                {
                    WriteBytes(stream, key);
                    Write(stream, item);
                }

                stream.WriteByte((byte)'e');
                break;
            case SortedDictionary<string, object> tree:
                Write(stream, tree.Select(entry => ((object)entry.Key, entry.Value)).ToList());
                break;
            case IEnumerable<object> items:
                stream.WriteByte((byte)'l');
                foreach (var item in items)
                    Write(stream, item);
                stream.WriteByte((byte)'e');
                break;
            default:
                throw new ArgumentException($"Can't bencode a {value.GetType().Name}.", nameof(value));
        }
    }

    private static byte[] KeyBytes(object key) => key is string text ? Encoding.UTF8.GetBytes(text) : (byte[])key;

    private static void WriteBytes(MemoryStream stream, byte[] bytes)
    {
        WriteAscii(stream, $"{bytes.Length.ToString(CultureInfo.InvariantCulture)}:");
        stream.Write(bytes);
    }

    private static void WriteAscii(MemoryStream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));

    /// <summary>The SHA-1 of every piece of <paramref name="data"/>, the last one as short as it is.</summary>
    private static byte[] Pieces(byte[] data, int pieceLength)
    {
        var pieces = new List<byte>();
        for (var start = 0; start < data.Length; start += pieceLength)
            pieces.AddRange(SHA1.HashData(data.AsSpan(start, Math.Min(pieceLength, data.Length - start))));
        return [.. pieces];
    }

    /// <summary>A file's pieces root: the tree over all its blocks, padded with zero leaves to a power of two.</summary>
    private static byte[] FileRoot(byte[] content) => Root(BlockHashes(content, 0, content.Length), PowerOfTwoAtLeast(Blocks(content.Length)));

    /// <summary>A file's piece layer: the root of each piece's own tree, the last piece's padded to a whole piece.</summary>
    private static byte[] PieceLayer(byte[] content, int pieceLength)
    {
        var layer = new List<byte>();
        for (var start = 0; start < content.Length; start += pieceLength)
            layer.AddRange(Root(BlockHashes(content, start, Math.Min(pieceLength, content.Length - start)), pieceLength / BlockSize));
        return [.. layer];
    }

    private static List<byte[]> BlockHashes(byte[] content, int start, int length)
    {
        var hashes = new List<byte[]>();
        for (var offset = 0; offset < length; offset += BlockSize)
            hashes.Add(SHA256.HashData(content.AsSpan(start + offset, Math.Min(BlockSize, length - offset))));
        return hashes;
    }

    /// <summary>The root over <paramref name="leaves"/> and zero leaves up to <paramref name="width"/>, level by level.</summary>
    private static byte[] Root(List<byte[]> leaves, int width)
    {
        var level = leaves.Concat(Enumerable.Range(0, width - leaves.Count).Select(_ => new byte[32])).ToList();
        while (level.Count > 1)
            level = [.. level.Chunk(2).Select(pair => SHA256.HashData([.. pair[0], .. pair[1]]))];
        return level[0];
    }

    private static int Blocks(int length) => Math.Max(1, (length + BlockSize - 1) / BlockSize);

    private static int PowerOfTwoAtLeast(int count)
    {
        var power = 1;
        while (power < count)
            power *= 2;
        return power;
    }

    /// <summary>Bencode's key order: by raw bytes.</summary>
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static ByteOrder Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
