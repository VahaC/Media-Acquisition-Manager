using System.Globalization;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Works out the info hashes of a release's payload (MAM-13): from a .torrent file, the hashes of its info
/// dictionary's bytes exactly as they were encoded; from a magnet link, the <c>xt</c> topics it names.
/// </summary>
/// <remarks>
/// <para>
/// The payload's own hashes, never the listing's claim: a feed states one rarely (no Toloka listing does)
/// and can state a wrong one, while a download client names the job by what it was given. They confirm a
/// submission against the client's job list when the client gives no id (specification §3.2, step 6).
/// </para>
/// <para>
/// A .torrent comes from a tracker the owner doesn't control, through a gateway that passes its bytes on
/// unchanged, so it is read defensively: nesting is bounded, every length is checked against the bytes
/// that are there, and anything that isn't bencode as BEP 3 writes it gives no hash rather than a guess or
/// an exception. A torrent has v1 metadata when its info dictionary has <c>pieces</c> and v2 metadata when
/// its <c>meta version</c> is 2 (BEP 52); a hybrid has both, and is hashed both ways.
/// </para>
/// </remarks>
public static class TorrentInfoHashReader
{
    /// <summary>
    /// Deeper than any real torrent nests (a v2 file tree is a folder level each, and libtorrent itself
    /// refuses past 100), shallow enough that a hostile file can't exhaust the stack.
    /// </summary>
    private const int MaximumDepth = 100;

    /// <summary>A string's length is at most this many digits: 10 digits already exceed any payload the
    /// app takes (4 MiB), and an <see cref="int"/> can't hold more.</summary>
    private const int LengthMaximumDigits = 10;

    /// <summary>A v2 magnet's topic is a multihash: 0x12 for SHA-256, then 0x20 for its 32 bytes.</summary>
    private const string Sha256MultihashPrefix = "1220";

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>The hashes of <paramref name="payload"/>, or <see cref="TorrentInfoHashes.None"/> when it
    /// is not a torrent or names none the app can read.</summary>
    public static TorrentInfoHashes Read(ReleasePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Kind switch
        {
            ReleasePayloadKind.TorrentFile when payload.TorrentFile is { } torrent => FromTorrentFile(torrent),
            ReleasePayloadKind.Magnet when payload.MagnetUri is { } magnet => FromMagnet(magnet),
            _ => TorrentInfoHashes.None,
        };
    }

    /// <summary>The hashes of a .torrent file's info dictionary.</summary>
    public static TorrentInfoHashes FromTorrentFile(ReadOnlySpan<byte> torrent)
    {
        if (!TryFindInfo(torrent, out var start, out var length))
            return TorrentInfoHashes.None;

        var info = torrent.Slice(start, length);
        if (!TryInspectInfo(info, out var hasPieces, out var metaVersion))
            return TorrentInfoHashes.None;

        // The bytes as they are in the file, never re-encoded: re-encoding would sort or normalise what a
        // careless encoder wrote, and the client hashes what it was given.
        var v1 = hasPieces ? Convert.ToHexStringLower(SHA1.HashData(info)) : null;
        var v2 = metaVersion == 2 ? Convert.ToHexStringLower(SHA256.HashData(info)) : null;
        return new TorrentInfoHashes(v1, v2);
    }

    /// <summary>
    /// The hashes a magnet link's exact topics name: <c>urn:btih:</c> with 40 hexadecimal or 32 base32
    /// characters for v1, <c>urn:btmh:1220</c> with 64 hexadecimal characters for v2. Two different
    /// hashes of one version in one link leave that version unknown rather than picking one.
    /// </summary>
    public static TorrentInfoHashes FromMagnet(string magnet)
    {
        ArgumentNullException.ThrowIfNull(magnet);
        if (!magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            return TorrentInfoHashes.None;

        string? v1 = null;
        string? v2 = null;
        var v1Conflicts = false;
        var v2Conflicts = false;
        foreach (var pair in magnet["magnet:?".Length..].Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || !IsExactTopic(pair[..separator]))
                continue;

            var topic = Uri.UnescapeDataString(pair[(separator + 1)..]);
            if (topic.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase) && V1Of(topic["urn:btih:".Length..]) is { } hash1)
                Take(ref v1, ref v1Conflicts, hash1);
            else if (topic.StartsWith("urn:btmh:", StringComparison.OrdinalIgnoreCase) && V2Of(topic["urn:btmh:".Length..]) is { } hash2)
                Take(ref v2, ref v2Conflicts, hash2);
        }

        return new TorrentInfoHashes(v1Conflicts ? null : v1, v2Conflicts ? null : v2);

        static void Take(ref string? kept, ref bool conflicts, string hash)
        {
            if (kept is not null && !string.Equals(kept, hash, StringComparison.Ordinal))
                conflicts = true;
            kept ??= hash;
        }
    }

    // ── magnet topics ───────────────────────────────────────────────────────────────────────────────

    /// <summary><c>xt</c>, or <c>xt.1</c>, <c>xt.2</c>… where a link names several topics (BEP 9).</summary>
    private static bool IsExactTopic(string name) =>
        string.Equals(name, "xt", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("xt.", StringComparison.OrdinalIgnoreCase)
            && name.Length > 3
            && name.AsSpan(3).IndexOfAnyExceptInRange('0', '9') < 0);

    private static string? V1Of(string value)
    {
        if (value.Length == 40 && IsHex(value))
            return value.ToLowerInvariant();
        if (value.Length == 32 && TryDecodeBase32(value, out var bytes))
            return Convert.ToHexStringLower(bytes);
        return null;
    }

    private static string? V2Of(string value) =>
        value.Length == Sha256MultihashPrefix.Length + 64
        && value.StartsWith(Sha256MultihashPrefix, StringComparison.Ordinal)
        && IsHex(value)
            ? value[Sha256MultihashPrefix.Length..].ToLowerInvariant()
            : null;

    /// <summary>ASCII hexadecimal only: <see cref="char.IsAsciiHexDigit"/> takes no other script's digits.</summary>
    private static bool IsHex(string value) => value.All(char.IsAsciiHexDigit);

    /// <summary>RFC 4648 base32 without padding, in either letter case: 32 characters are the 20 bytes of a
    /// v1 hash.</summary>
    private static bool TryDecodeBase32(string value, out byte[] bytes)
    {
        bytes = new byte[value.Length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var character in value)
        {
            var digit = Base32Alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            if (digit < 0)
                return false;
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return index == bytes.Length;
    }

    // ── bencode ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Where the top-level dictionary's <c>info</c> value starts and how long it is, when the
    /// file is a dictionary holding one that is itself a dictionary.</summary>
    private static bool TryFindInfo(ReadOnlySpan<byte> torrent, out int start, out int length)
    {
        start = 0;
        length = 0;
        if (torrent.IsEmpty || torrent[0] != (byte)'d')
            return false;

        var position = 1;
        while (position < torrent.Length && torrent[position] != (byte)'e')
        {
            if (!TryReadString(torrent, ref position, out var keyStart, out var keyLength))
                return false;

            var valueStart = position;
            if (!TrySkip(torrent, ref position, depth: 1))
                return false;

            if (torrent.Slice(keyStart, keyLength).SequenceEqual("info"u8))
            {
                if (torrent[valueStart] != (byte)'d')
                    return false;
                start = valueStart;
                length = position - valueStart;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the info dictionary has <c>pieces</c> (v1) and what its <c>meta version</c> is (v2 is 2);
    /// false when it isn't a well-formed dictionary.
    /// </summary>
    private static bool TryInspectInfo(ReadOnlySpan<byte> info, out bool hasPieces, out long? metaVersion)
    {
        hasPieces = false;
        metaVersion = null;
        var position = 1;
        while (position < info.Length && info[position] != (byte)'e')
        {
            if (!TryReadString(info, ref position, out var keyStart, out var keyLength))
                return false;

            var key = info.Slice(keyStart, keyLength);
            var valueStart = position;
            if (!TrySkip(info, ref position, depth: 2))
                return false;

            var value = info[valueStart..position];
            if (key.SequenceEqual("pieces"u8) && value[0] is >= (byte)'0' and <= (byte)'9')
                hasPieces = true;
            else if (key.SequenceEqual("meta version"u8) && value[0] == (byte)'i' && TryReadInteger(value, out var version))
                metaVersion = version;
        }

        return position == info.Length - 1;
    }

    /// <summary>Moves past one value of any type at <paramref name="position"/>; false when there is none
    /// to read, it runs past the end, or it nests deeper than <see cref="MaximumDepth"/>.</summary>
    private static bool TrySkip(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        if (position >= data.Length || depth > MaximumDepth)
            return false;

        switch (data[position])
        {
            case (byte)'i':
            {
                var end = data[position..].IndexOf((byte)'e');
                if (end < 0 || !TryReadInteger(data.Slice(position, end + 1), out _))
                    return false;
                position += end + 1;
                return true;
            }
            case (byte)'l':
                position++;
                while (position < data.Length && data[position] != (byte)'e')
                {
                    if (!TrySkip(data, ref position, depth + 1))
                        return false;
                }

                return TryClose(data, ref position);
            case (byte)'d':
                position++;
                while (position < data.Length && data[position] != (byte)'e')
                {
                    // A dictionary's keys are strings; anything else is not bencode.
                    if (!TryReadString(data, ref position, out _, out _) || !TrySkip(data, ref position, depth + 1))
                        return false;
                }

                return TryClose(data, ref position);
            case >= (byte)'0' and <= (byte)'9':
                return TryReadString(data, ref position, out _, out _);
            default:
                return false;
        }
    }

    /// <summary>Moves past a list's or dictionary's closing <c>e</c>, when the data hasn't ended first.</summary>
    private static bool TryClose(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
            return false;
        position++;
        return true;
    }

    /// <summary>A string <c>&lt;length&gt;:&lt;bytes&gt;</c>: where its bytes start and how many there are,
    /// moving past it; false when its length is malformed or runs past the end.</summary>
    private static bool TryReadString(ReadOnlySpan<byte> data, ref int position, out int start, out int length)
    {
        start = 0;
        length = 0;
        var colon = data[position..].IndexOf((byte)':');
        if (colon is < 1 or > LengthMaximumDigits)
            return false;

        var digits = data.Slice(position, colon);
        if (digits.IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0
            || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var declared))
            return false;

        var bytesStart = position + colon + 1;
        if (declared > data.Length - bytesStart)
            return false;

        start = bytesStart;
        length = (int)declared;
        position = bytesStart + length;
        return true;
    }

    /// <summary>An integer <c>i&lt;digits&gt;e</c>, with an optional minus sign, that fits a <see cref="long"/>.</summary>
    private static bool TryReadInteger(ReadOnlySpan<byte> value, out long integer)
    {
        integer = 0;
        if (value.Length < 3 || value[0] != (byte)'i' || value[^1] != (byte)'e')
            return false;

        var digits = value[1..^1];
        return long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out integer)
            && digits.IndexOfAnyExcept("-0123456789"u8) < 0;
    }
}
