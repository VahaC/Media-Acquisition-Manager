using System.Globalization;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — reads bencode (BEP 3) into <see cref="BencodeNode"/>s, for the parts of a .torrent the overwrite
/// check needs: its names, lengths and piece hashes.
/// </summary>
/// <remarks>
/// A .torrent comes from a tracker the owner doesn't control, so it is read as <see cref="TorrentInfoHashReader"/>
/// reads it: nesting is bounded, every length is checked against the bytes that are there, and anything that isn't
/// bencode gives no value rather than a guess or an exception. The number of values is bounded too, so a hostile
/// file of tiny values can't take the memory a tree of them needs.
/// </remarks>
public static class BencodeParser
{
    /// <summary>libtorrent refuses deeper; a v2 file tree is a level per folder.</summary>
    private const int MaximumDepth = 100;

    /// <summary>
    /// A torrent of a hundred thousand files has about half a million values; ten times the app's 4 MiB payload cap
    /// in values could not be real.
    /// </summary>
    private const int MaximumValues = 1_000_000;

    /// <summary>A length has at most this many digits: 10 already exceed any payload the app takes.</summary>
    private const int LengthMaximumDigits = 10;

    /// <summary>The value that starts <paramref name="data"/>; bytes after it are ignored, as libtorrent ignores
    /// them. False when the start is no bencoded value.</summary>
    public static bool TryParse(ReadOnlyMemory<byte> data, out BencodeNode? value)
    {
        var reader = new Reader(data);
        var position = 0;
        value = reader.Read(ref position, depth: 1);
        return value is not null;
    }

    private sealed class Reader(ReadOnlyMemory<byte> data)
    {
        private int values;

        public BencodeNode? Read(ref int position, int depth)
        {
            var span = data.Span;
            if (position >= span.Length || depth > MaximumDepth || ++values > MaximumValues)
                return null;

            var start = position;
            switch (span[position])
            {
                case (byte)'i':
                {
                    var end = span[position..].IndexOf((byte)'e');
                    if (end < 2 || !TryReadInteger(span.Slice(position + 1, end - 1), out var integer))
                        return null;
                    position += end + 1;
                    return new BencodeNode(BencodeKind.Integer, data[start..position], integer, default, [], []);
                }
                case (byte)'l':
                {
                    position++;
                    var items = new List<BencodeNode>();
                    while (position < span.Length && span[position] != (byte)'e')
                    {
                        if (Read(ref position, depth + 1) is not { } item)
                            return null;
                        items.Add(item);
                    }

                    if (position >= span.Length)
                        return null;
                    position++;
                    return new BencodeNode(BencodeKind.List, data[start..position], 0, default, items, []);
                }
                case (byte)'d':
                {
                    position++;
                    var keys = new List<ReadOnlyMemory<byte>>();
                    var items = new List<BencodeNode>();
                    while (position < span.Length && span[position] != (byte)'e')
                    {
                        // A dictionary's keys are strings; anything else is not bencode.
                        if (!TryReadBytes(ref position, out var key) || Read(ref position, depth + 1) is not { } item)
                            return null;
                        keys.Add(key);
                        items.Add(item);
                    }

                    if (position >= span.Length)
                        return null;
                    position++;
                    return new BencodeNode(BencodeKind.Dictionary, data[start..position], 0, default, items, keys);
                }
                case >= (byte)'0' and <= (byte)'9':
                    return TryReadBytes(ref position, out var bytes)
                        ? new BencodeNode(BencodeKind.Bytes, data[start..position], 0, bytes, [], [])
                        : null;
                default:
                    return null;
            }
        }

        /// <summary>A string <c>&lt;length&gt;:&lt;bytes&gt;</c>: its bytes, moving past it.</summary>
        private bool TryReadBytes(ref int position, out ReadOnlyMemory<byte> bytes)
        {
            bytes = default;
            var span = data.Span;
            var colon = span[position..].IndexOf((byte)':');
            if (colon is < 1 or > LengthMaximumDigits)
                return false;

            var digits = span.Slice(position, colon);
            if (digits.IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0
                || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var declared))
                return false;

            var bytesStart = position + colon + 1;
            if (declared > span.Length - bytesStart)
                return false;

            bytes = data.Slice(bytesStart, (int)declared);
            position = bytesStart + (int)declared;
            return true;
        }

        /// <summary>The digits of <c>i…e</c>, with an optional minus sign, that fit a <see cref="long"/>.</summary>
        private static bool TryReadInteger(ReadOnlySpan<byte> digits, out long integer) =>
            long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out integer)
            && digits.IndexOfAnyExcept("-0123456789"u8) < 0;
    }
}
