using System.Text;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-38 — one element of a torrent's path cleaned as libtorrent 2.0 cleans it on Linux before it writes the file
// (sanitize_append_path_element, src/torrent_info.cpp, RC_2_0), so the overwrite check looks where the client will
// write. A forced element (a file's path element, a file tree's key) is always one element: `.`, `..` and an
// element cleaning leaves empty are `_`; the torrent's name, not forced, drops them. Control characters become `_`,
// a NUL, slashes and bidirectional controls are dropped, DEL and the C1 controls stay; a UTF-8 sequence that isn't
// valid is one `_` for the whole length its lead byte claims; past 240 bytes an element ends after the character
// that reached them, with an extension from a dot among its last 9 bytes.
public sealed class TorrentPathElementTests
{
    public static TheoryData<string, byte[], bool, string?> Elements => new()
    {
        { "a plain name stays", Encoding.UTF8.GetBytes("Фільм (2001).mkv"), false, "Фільм (2001).mkv" },
        { "a plain path element stays", Encoding.UTF8.GetBytes("Фільм (2001).mkv"), true, "Фільм (2001).mkv" },
        { "a dot as the name is dropped", "."u8.ToArray(), false, null },
        { "a dot as a path element is an underscore", "."u8.ToArray(), true, "_" },
        { "two dots as the name are dropped", ".."u8.ToArray(), false, null },
        { "two dots as a path element are an underscore", ".."u8.ToArray(), true, "_" },
        { "three dots stay", "..."u8.ToArray(), true, "..." },
        { "an empty name is an underscore", [], false, "_" },
        { "an empty path element is an underscore", [], true, "_" },
        { "a name cleaning leaves empty is dropped", "/"u8.ToArray(), false, null },
        { "a path element cleaning leaves empty is an underscore", "/\\"u8.ToArray(), true, "_" },
        { "a name cleaning leaves a dot is dropped", "/."u8.ToArray(), false, null },
        { "a control character is an underscore", "a\u0001b"u8.ToArray(), true, "a_b" },
        { "a NUL is dropped", [(byte)'a', 0, (byte)'b'], true, "ab" },
        { "slashes are dropped", "a/b\\c"u8.ToArray(), true, "abc" },
        { "bidirectional controls are dropped", Encoding.UTF8.GetBytes("‎abc‮"), true, "abc" },
        { "DEL and a C1 control stay", Encoding.UTF8.GetBytes("a\u007Fb\u0085c"), true, "a\u007Fb\u0085c" },
        { "a byte that starts no sequence is one underscore", [(byte)'a', 0xFF, (byte)'b'], true, "a_b" },
        { "a stray continuation byte is one underscore", [(byte)'a', 0x80, (byte)'b'], true, "a_b" },
        { "a broken sequence is one underscore for the length its lead byte claims", [(byte)'a', 0xE2, (byte)'b', (byte)'c', (byte)'d'], true, "a_d" },
        { "a sequence cut short by the end takes the rest", [(byte)'a', 0xF0, 0x9F], true, "a_" },
        { "a five-byte lead byte takes five bytes", [0xF8, (byte)'a', (byte)'b', (byte)'c', (byte)'d', (byte)'e'], true, "_e" },
        { "an overlong form is one underscore", [(byte)'a', 0xC0, 0x80, (byte)'b'], true, "a_b" },
        { "a surrogate is one underscore", [(byte)'a', 0xED, 0xA0, 0x80, (byte)'b'], true, "a_b" },
    };

    [Theory]
    [MemberData(nameof(Elements))]
    public void Sanitize_cleans_an_element_as_libtorrent_does(string label, byte[] raw, bool force, string? expected)
    {
        //Arrange
        _ = label;

        //Act
        var sanitized = TorrentPathElement.Sanitize(raw, force);

        //Assert
        Assert.Equal(expected, sanitized);
    }

    public static TheoryData<string, string, string> LongElements => new()
    {
        // Each ж is two bytes: the 120th reaches 240, and the extension follows.
        { "two-byte characters and an extension", new string('ж', 200) + ".mkv", new string('ж', 120) + ".mkv" },
        // One byte, then ж: the 120th ж crosses 240 and is kept whole, 241 bytes before the extension.
        { "a one-byte character before two-byte ones", "a" + new string('ж', 200) + ".mkv", "a" + new string('ж', 120) + ".mkv" },
        { "two-byte characters without an extension", new string('ж', 200), new string('ж', 120) },
        { "one-byte characters without an extension", new string('a', 300), new string('a', 240) },
        // The dot is the 10th byte from the end: too far to start an extension.
        { "a dot 10 bytes from the end", new string('a', 250) + ".extension", new string('a', 240) },
        { "a dot 9 bytes from the end", new string('a', 250) + ".extensio", new string('a', 240) + ".extensio" },
    };

    [Theory]
    [MemberData(nameof(LongElements))]
    public void Sanitize_ends_an_element_past_240_bytes_as_libtorrent_does(string label, string raw, string expected)
    {
        //Arrange
        _ = label;

        //Act
        var sanitized = TorrentPathElement.Sanitize(Encoding.UTF8.GetBytes(raw), force: true);

        //Assert
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void Sanitize_cleans_the_extension_it_keeps_past_240_bytes()
    {
        //Arrange
        byte[] raw = [.. Encoding.UTF8.GetBytes(new string('a', 245)), (byte)'.', (byte)'m', 0x01, (byte)'v'];

        //Act
        var sanitized = TorrentPathElement.Sanitize(raw, force: true);

        //Assert
        Assert.Equal(new string('a', 240) + ".m_v", sanitized);
    }
}
