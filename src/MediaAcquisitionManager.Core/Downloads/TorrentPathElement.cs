using System.Text;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — one element of a torrent's path cleaned the way libtorrent 2.0 cleans it on Linux before it writes a
/// file (<c>sanitize_append_path_element</c> in <c>src/torrent_info.cpp</c>, branch RC_2_0), so the path the
/// overwrite check looks at is the one the client will write.
/// </summary>
/// <remarks>
/// <para>
/// A port of libtorrent's loop, byte by byte. Each UTF-8 sequence is read as its <c>parse_utf8_codepoint</c> reads
/// it: the length comes from the lead byte, and a sequence that isn't valid takes the whole length it claims (the
/// rest of the element when that is shorter, five bytes for a lead byte of 0xF8 to 0xFB, one byte for a lead byte
/// that claims none) and becomes one <c>_</c>. A character below 32 becomes <c>_</c> too, but NUL; NUL, <c>/</c>,
/// <c>\</c> and the bidirectional controls are dropped; DEL and the C1 controls stay, as on Linux they are valid.
/// </para>
/// <para>
/// Once 240 bytes are written — the character that reaches it kept whole — a dot among the last 9 bytes of the raw
/// element is looked for: with none the element ends there, with one the rest is written from that dot on, still
/// cleaned. An element can so be 240 bytes and more, its extension after them.
/// </para>
/// <para>
/// <c>force</c> is libtorrent's <c>force_element</c>: true for each element of a v1 file's <c>path</c> and each key
/// of a v2 file tree, which always add one element — <c>.</c>, <c>..</c> and an element cleaning leaves empty
/// become <c>_</c> —; false for the torrent's name, which they drop. An empty element is <c>_</c> either way.
/// </para>
/// <para>
/// A name this reads differently from libtorrent can only make the check look at another path than the client
/// writes: a collision missed, as before the check existed, or a send refused for a file the client wouldn't have
/// touched.
/// </para>
/// </remarks>
public static class TorrentPathElement
{
    /// <summary>The bytes written before libtorrent looks for an extension and stops.</summary>
    private const int MaximumBytes = 240;

    /// <summary>A dot farther than this from the element's end, counted as libtorrent counts it
    /// (<c>j &gt; size - 10</c>), starts no extension: one is at most 9 bytes, its dot included.</summary>
    private const int ExtensionSearchBytes = 10;

    /// <summary>The highest code point, as libtorrent's <c>max_codepoint</c>.</summary>
    private const int MaximumCodePoint = 0x10FFFF;

    /// <summary>The element as the client writes it; <c>null</c> when libtorrent drops it, which only an element
    /// not forced can be.</summary>
    /// <param name="raw">The element's bytes, as the torrent holds them.</param>
    /// <param name="force">libtorrent's <c>force_element</c>: true for a file's path element or a file tree's key,
    /// false for the torrent's name.</param>
    public static string? Sanitize(ReadOnlySpan<byte> raw, bool force)
    {
        if (raw.Length == 1 && raw[0] == (byte)'.' && !force)
            return null;
        if (raw.IsEmpty)
            return "_";

        var written = new List<byte>(raw.Length);
        var added = 0;
        var dots = 0;
        var foundExtension = false;
        for (var index = 0; index < raw.Length;)
        {
            var start = index;
            var (codePoint, length) = Decode(raw[start..]);
            index += length;

            if (codePoint >= 0 && IsFiltered(codePoint))
                continue;
            if (codePoint < 32)
            {
                // Not UTF-8 (-1), or a control character libtorrent refuses on Linux.
                written.Add((byte)'_');
                added++;
                continue;
            }

            for (var offset = start; offset < start + length; offset++)
                written.Add(raw[offset]);
            if (codePoint == '.')
                dots++;
            added += length;

            // libtorrent looks only after a character it wrote as it was, never after an underscore.
            if (added >= MaximumBytes && !foundExtension)
            {
                var dot = -1;
                for (var candidate = raw.Length - 1; candidate > Math.Max(raw.Length - ExtensionSearchBytes, start); candidate--)
                {
                    if (raw[candidate] == (byte)'.')
                    {
                        dot = candidate;
                        break;
                    }
                }

                if (dot == -1)
                    break;
                foundExtension = true;
                index = dot;
            }
        }

        // ".", ".." or nothing at all: replaced by an underscore when forced, else dropped.
        if (added == dots && added <= 2)
            return force ? "_" : null;

        return Encoding.UTF8.GetString([.. written]);
    }

    /// <summary>
    /// The code point at the start of <paramref name="text"/> and how many bytes it takes, as libtorrent's
    /// <c>parse_utf8_codepoint</c> gives them: -1 for a sequence that isn't valid UTF-8 — a lead byte that starts
    /// none, a byte after it that doesn't continue one, an overlong form, a surrogate or a code point past
    /// U+10FFFF —, with the length the lead byte claims.
    /// </summary>
    private static (int CodePoint, int Length) Decode(ReadOnlySpan<byte> text)
    {
        var lead = text[0];
        var length = lead < 0b1000_0000 ? 1
            : lead >> 5 == 0b110 ? 2
            : lead >> 4 == 0b1110 ? 3
            : lead >> 3 == 0b11110 ? 4
            // Not a lead byte any more, but libtorrent still skips that many bytes.
            : lead >> 2 == 0b111110 ? 5
            : 0;

        if (length == 1)
            return (lead, 1);
        if (length == 0)
            return (-1, 1);
        if (length > text.Length)
            return (-1, text.Length);
        if (length > 4)
            return (-1, length);

        var codePoint = lead & (length == 2 ? 0b0001_1111 : length == 3 ? 0b0000_1111 : 0b0000_0111);
        for (var index = 1; index < length; index++)
        {
            var next = text[index];
            if (next is < 0b1000_0000 or > 0b1011_1111)
                return (-1, length);
            codePoint = (codePoint << 6) + (next & 0b0011_1111);
        }

        var overlong = length switch
        {
            2 => codePoint < 0x80,
            3 => codePoint < 0x800,
            _ => codePoint < 0x10000,
        };
        if (overlong || codePoint > MaximumCodePoint || codePoint is >= 0xD800 and <= 0xDFFF)
            return (-1, length);

        return (codePoint, length);
    }

    /// <summary>
    /// libtorrent's <c>filter_path_character</c>: the bidirectional controls, which can make a name read as another,
    /// and the separators — and NUL, as its <c>strchr</c> finds the string's own terminator for 0, so a NUL byte is
    /// dropped rather than made an underscore.
    /// </summary>
    private static bool IsFiltered(int codePoint) =>
        codePoint is 0 or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or '/' or '\\';
}
