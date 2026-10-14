using System.Collections.ObjectModel;
using System.Text;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-30 — the three ways a torrent's name, or one element of a file's path in it, becomes folder names on disk in a
/// client that writes the torrent's own layout (<see cref="Abstractions.Downloads.TorrentFilePlacement.OwnLayoutOnly"/>),
/// so the overwrite check looks where that client will write (ADR 0034). Each answers the folder names in order: the
/// text is split on <c>/</c> first, so a name like <c>x/y.mkv</c> is a folder and a file, never one name.
/// </summary>
/// <remarks>
/// <para>
/// Read from Transmission's source (MAM-29's samples, ADR 0039): 4.0 cleans every name for any operating system
/// (<c>makeSubpathPortable</c> in <c>libtransmission/torrent-files.cc</c>, 4.0.6) — <see cref="Portable"/>; 4.1 on
/// Windows cleans it the same way and also replaces control characters (<c>sanitize_subpath</c> with
/// <c>is_win32_reserved_char</c>, 4.1.3) — <see cref="WindowsSafe"/>; 4.1 on Linux keeps a name as it is but for
/// <c>.</c> and <c>..</c> (<c>sanitize_subpath</c>, 4.1.3) — <see cref="Verbatim"/>. Which one a client uses is its
/// adapter's to say (version and the style of its folder), not this class's.
/// </para>
/// <para>
/// Both take text already decoded from UTF-8, a sequence that isn't valid read as U+FFFD (as Transmission's
/// <c>tr_strv_convert_utf8</c> does), and both cut it at the first NUL. A lone surrogate, which no UTF-8 decodes to,
/// is read as U+FFFD too. No answer ever holds an empty name, <c>.</c>, <c>..</c> or a name with <c>/</c>, which would
/// make a path name another place than the one the client writes (<see cref="Paths.NormalizedPath.Append"/> checks
/// nothing).
/// </para>
/// </remarks>
public static class TorrentNameReadings
{
    /// <summary>The characters Transmission 4.0 replaces with <c>_</c>: those a Windows name can't hold, but <c>/</c>,
    /// which splits the text first.</summary>
    private const string ReservedCharacters = "\"*:<>?\\|";

    /// <summary>The names Windows reserves for devices; Transmission 4.0 puts <c>_</c> before one, alone or followed
    /// by a dot and anything.</summary>
    private static readonly string[] ReservedNames =
    [
        "AUX", "CON", "NUL", "PRN",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Transmission 4.0's reading, on every operating system: each piece between <c>/</c> loses its leading and
    /// trailing whitespace (C's <c>isspace</c>: space, tab, line feed, vertical tab, form feed, carriage return), then
    /// its trailing dots; a reserved device name gets <c>_</c> before it; each of <c>" * : &lt; &gt; ? \ |</c> becomes
    /// <c>_</c>, while a control character inside a piece stays. A piece left empty is dropped, as <c>a//b</c> is
    /// <c>a/b</c> on disk — so <c>...</c> and <c>..</c> are nothing, and a text that is only such pieces answers none.
    /// </summary>
    /// <param name="text">The torrent's name or one element of a file's path, as decoded.</param>
    public static IReadOnlyList<string> Portable(string text) => Cleaned(text, replacesControlCharacters: false);

    /// <summary>
    /// Transmission 4.1's reading on Windows: <see cref="Portable"/>'s, and each control character still in a piece
    /// (U+0001 to U+001F, which a Windows name can't hold either) becomes <c>_</c> as well — after the whitespace at
    /// either end is gone, so <c>a\tb</c> is <c>a_b</c> but <c>\tname</c> is <c>name</c>. A piece left empty is
    /// dropped; a text that is only such pieces answers none.
    /// </summary>
    /// <param name="text">The torrent's name or one element of a file's path, as decoded.</param>
    public static IReadOnlyList<string> WindowsSafe(string text) => Cleaned(text, replacesControlCharacters: true);

    /// <summary>
    /// Transmission 4.1's reading on Linux: each piece between <c>/</c> as it is, spaces, dots and colons kept, but a
    /// piece that is exactly <c>.</c> or <c>..</c>, which becomes <c>_.</c> or <c>_..</c>. A piece left empty is dropped.
    /// None at all names no place, as with the other readings: a file's path that reads as nothing is a torrent
    /// Transmission refuses ("invalid path"), and a torrent's name that does is no folder of its own.
    /// </summary>
    /// <param name="text">The torrent's name or one element of a file's path, as decoded.</param>
    public static IReadOnlyList<string> Verbatim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var names = new List<string>();
        foreach (var piece in Pieces(text))
        {
            if (piece.Length == 0)
                continue;
            names.Add(piece is "." or ".." ? "_" + piece : piece);
        }

        return names.AsReadOnly();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Each piece stripped of whitespace at either end, then of trailing dots, a reserved device name marked, the
    /// reserved characters — and with <paramref name="replacesControlCharacters"/> the control characters too —
    /// replaced; the empty pieces dropped.
    /// </summary>
    private static ReadOnlyCollection<string> Cleaned(string text, bool replacesControlCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);

        var names = new List<string>();
        foreach (var piece in Pieces(text))
        {
            var cleaned = piece.AsSpan().Trim(" \t\n\v\f\r").TrimEnd('.');
            if (cleaned.IsEmpty)
                continue;

            var name = new StringBuilder(cleaned.Length + 1);
            if (IsReservedName(cleaned))
                name.Append('_');
            foreach (var character in cleaned)
            {
                var replaced = ReservedCharacters.Contains(character, StringComparison.Ordinal)
                    || (replacesControlCharacters && character <= '\u001F');
                name.Append(replaced ? '_' : character);
            }

            names.Add(name.ToString());
        }

        return names.AsReadOnly();
    }

    /// <summary>The text cut at its first NUL, each lone surrogate read as U+FFFD, split on <c>/</c>.</summary>
    private static string[] Pieces(string text)
    {
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        var kept = end < 0 ? text : text[..end];

        var decoded = new StringBuilder(kept.Length);
        for (var index = 0; index < kept.Length; index++)
        {
            var character = kept[index];
            if (char.IsHighSurrogate(character) && index + 1 < kept.Length && char.IsLowSurrogate(kept[index + 1]))
            {
                decoded.Append(character).Append(kept[index + 1]);
                index++;
            }
            else
            {
                decoded.Append(char.IsSurrogate(character) ? '�' : character);
            }
        }

        return decoded.ToString().Split('/');
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a reserved device name, alone or followed by a dot, in any letter case — the
    /// ASCII letters only, as C's <c>toupper</c> reads them, so no other script's letter ever upper-cases into one.
    /// </summary>
    private static bool IsReservedName(ReadOnlySpan<char> name)
    {
        foreach (var reserved in ReservedNames)
        {
            if (name.Length < reserved.Length || !IsAsciiSameIgnoringCase(name[..reserved.Length], reserved))
                continue;
            if (name.Length == reserved.Length || name[reserved.Length] == '.')
                return true;
        }

        return false;
    }

    private static bool IsAsciiSameIgnoringCase(ReadOnlySpan<char> text, string reserved)
    {
        for (var index = 0; index < reserved.Length; index++)
        {
            var character = text[index];
            var upper = character is >= 'a' and <= 'z' ? (char)(character - 'a' + 'A') : character;
            if (upper != reserved[index])
                return false;
        }

        return true;
    }
}
