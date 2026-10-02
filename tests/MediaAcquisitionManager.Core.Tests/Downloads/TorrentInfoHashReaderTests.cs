using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-13 — the info hashes the app works out from a payload, by which a submission is found in the client's
// job list when the client gives no id (specification §3.2, step 6). A .torrent is hashed over its info
// dictionary's bytes exactly as written — v1 with SHA-1 when it has `pieces`, v2 with SHA-256 when its
// `meta version` is 2 (BEP 52) — and a magnet by the topics it names. The expected hashes were computed
// outside the app (Python's hashlib) over the same bytes. A file from a tracker is hostile until read:
// everything malformed gives no hash, never an exception or a guess.
public sealed class TorrentInfoHashReaderTests
{
    private const string V1Info = "d6:lengthi1e4:name5:x.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaae";
    private const string V1Hash = "72d6069eff259a0800f7afdb2500d5de591a9f01";
    private const string V1HashBase32 = "OLLANHX7EWNAQAHXV7NSKAGV3ZMRVHYB";

    private const string V2Info =
        "d9:file treed5:x.mkvd0:d6:lengthi1e11:pieces root32:rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrreee12:meta versioni2e4:name5:x.mkv12:piece lengthi16384ee";
    private const string V2Hash = "3d8c42fa5353329627f1a2afcba8bc04c6b5db714d3b07577c9624e6a4396bc2";

    private const string HybridInfo =
        "d9:file treed5:x.mkvd0:d6:lengthi1e11:pieces root32:rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrreee6:lengthi1e12:meta versioni2e4:name5:x.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaae";
    private const string HybridV1Hash = "aaeeecd05bc4058a5dd980de3cd8c7c1794034bc";
    private const string HybridV2Hash = "b29a07ddcde6736c607836fc557abceaaaf64a860607c374fbc2d9de02ffc274";

    [Fact]
    public void FromTorrentFile_of_a_v1_torrent_is_the_sha1_of_its_info_dictionary()
    {
        //Arrange
        var torrent = Torrent(V1Info);

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(new TorrentInfoHashes(V1Hash, null), hashes);
    }

    [Fact]
    public void FromTorrentFile_of_a_v2_torrent_is_the_sha256_of_its_info_dictionary_and_no_v1()
    {
        //Arrange
        var torrent = Torrent(V2Info);

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(new TorrentInfoHashes(null, V2Hash), hashes);
        Assert.Equal(V2Hash, hashes.Primary);
    }

    [Fact]
    public void FromTorrentFile_of_a_hybrid_torrent_is_both_hashes_and_names_it_by_v1()
    {
        //Arrange
        var torrent = Torrent(HybridInfo);

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(new TorrentInfoHashes(HybridV1Hash, HybridV2Hash), hashes);
        Assert.Equal(HybridV1Hash, hashes.Primary);
        Assert.Equal([HybridV1Hash, HybridV2Hash], hashes.All);
    }

    [Fact]
    public void FromTorrentFile_hashes_the_info_bytes_as_written_even_when_the_keys_are_out_of_order()
    {
        //Arrange
        // BEP 3 sorts a dictionary's keys; a careless encoder doesn't, and the client hashes what it got.
        var torrent = Torrent("d4:name5:x.mkv6:lengthi1e6:pieces20:aaaaaaaaaaaaaaaaaaaa12:piece lengthi16384ee");

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal("b5b4ba93fc6f4083c9151db98e11b19f29402894", hashes.V1);
    }

    [Fact]
    public void FromTorrentFile_finds_info_wherever_it_sits_among_the_top_level_keys()
    {
        //Arrange
        var torrent = Encoding.ASCII.GetBytes(
            "d8:announce19:http://tracker.test7:comment3:abc4:info" + V1Info + "13:creation datei1700000000e8:url-listl12:http://a.b/ce");

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(V1Hash, hashes.V1);
    }

    [Fact]
    public void FromTorrentFile_reads_the_sample_the_feed_fake_serves()
    {
        //Arrange
        var torrent = "d8:announce19:http://tracker.test4:infod6:lengthi1e4:name5:x.mkv12:piece lengthi16384e6:pieces20:aaaaaaaaaaaaaaaaaaaaee"u8.ToArray();

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(V1Hash, hashes.V1);
    }

    [Fact]
    public void FromTorrentFile_does_not_count_a_pieces_key_nested_below_the_info_dictionary()
    {
        //Arrange
        // Only the info dictionary's own keys say which metadata the torrent has.
        var torrent = Torrent("d5:filesld6:pieces3:abcee4:name5:x.mkve");

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(TorrentInfoHashes.None, hashes);
    }

    [Fact]
    public void FromTorrentFile_takes_only_meta_version_2_as_v2()
    {
        //Arrange
        var torrent = Torrent("d12:meta versioni3e4:name5:x.mkv6:pieces20:aaaaaaaaaaaaaaaaaaaae");

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.NotNull(hashes.V1);
        Assert.Null(hashes.V2);
    }

    public static TheoryData<string, string> MalformedTorrents => new()
    {
        { "empty", "" },
        { "not a dictionary", "l4:infoe" },
        { "an HTML login page", "<html><body>Sign in</body></html>" },
        { "no info", "d8:announce19:http://tracker.teste" },
        { "info that is a list", "d4:infoli1eee" },
        { "info that is a string", "d4:info3:abce" },
        { "a string longer than the file", "d4:infod6:pieces99:aaaee" },
        { "a length with eleven digits", "d4:infod6:pieces12345678901:ae" },
        { "a length that isn't a number", "d4:infod6:pieces2x:aae" },
        { "a key that isn't a string", "d4:infodi1e3:abcee" },
        { "an integer without digits", "d4:infod6:lengthie6:pieces1:aee" },
        { "an integer with letters", "d4:infod6:lengthi1x2e6:pieces1:aee" },
        { "an unterminated integer", "d4:infod6:lengthi12" },
        { "an unterminated info", "d4:infod6:pieces1:a" },
        { "an info left open after its last value", "d4:infod5:filesl6:pieces1:ae" },
        { "a stray byte inside info", "d4:infod6:pieces1:aXee" },
        { "a stray byte before info", "dX4:infod6:pieces1:aee" },
    };

    [Theory]
    [MemberData(nameof(MalformedTorrents))]
    public void FromTorrentFile_of_malformed_bytes_is_no_hash(string label, string torrent)
    {
        //Arrange
        var bytes = Encoding.ASCII.GetBytes(torrent);

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(bytes);

        //Assert
        Assert.True(hashes == TorrentInfoHashes.None, label);
    }

    [Fact]
    public void FromTorrentFile_of_nesting_deeper_than_any_torrent_is_no_hash_and_no_stack_overflow()
    {
        //Arrange
        // A hostile file nesting a million lists would overflow the stack of a reader without a bound.
        const int depth = 1_000_000;
        var torrent = Encoding.ASCII.GetBytes("d4:infod6:pieces1:a5:filesl" + new string('l', depth) + new string('e', depth) + "eee");

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.Equal(TorrentInfoHashes.None, hashes);
    }

    [Fact]
    public void FromTorrentFile_reads_nesting_up_to_a_real_torrents_depth()
    {
        //Arrange
        // A v2 file tree nests a dictionary per folder; 90 levels of folders still read.
        const int depth = 90;
        var torrent = Encoding.ASCII.GetBytes("d4:infod6:pieces1:a5:filesl" + new string('l', depth) + new string('e', depth) + "eee");

        //Act
        var hashes = TorrentInfoHashReader.FromTorrentFile(torrent);

        //Assert
        Assert.NotNull(hashes.V1);
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:" + V1Hash + "&dn=x.mkv", V1Hash)]
    [InlineData("magnet:?xt=urn:btih:72D6069EFF259A0800F7AFDB2500D5DE591A9F01", V1Hash)]
    [InlineData("magnet:?xt=urn:btih:" + V1HashBase32 + "&tr=http%3A%2F%2Ftracker.test%2Fannounce", V1Hash)]
    [InlineData("magnet:?dn=x.mkv&xt=urn:btih:ollanhx7ewnaqahxv7nskagv3zmrvhyb", V1Hash)]
    [InlineData("MAGNET:?XT=URN:BTIH:" + V1Hash, V1Hash)]
    [InlineData("magnet:?xt=urn%3Abtih%3A" + V1Hash, V1Hash)]
    [InlineData("magnet:?xt.1=urn:btih:" + V1Hash + "&xt.2=urn:sha1:abc", V1Hash)]
    public void FromMagnet_reads_a_v1_topic_in_hex_or_base32(string magnet, string expected)
    {
        //Arrange

        //Act
        var hashes = TorrentInfoHashReader.FromMagnet(magnet);

        //Assert
        Assert.Equal(new TorrentInfoHashes(expected, null), hashes);
    }

    [Fact]
    public void FromMagnet_reads_a_v2_topic_as_a_sha256_multihash()
    {
        //Arrange
        var magnet = "magnet:?xt=urn:btmh:1220" + V2Hash.ToUpperInvariant() + "&dn=x.mkv";

        //Act
        var hashes = TorrentInfoHashReader.FromMagnet(magnet);

        //Assert
        Assert.Equal(new TorrentInfoHashes(null, V2Hash), hashes);
    }

    [Fact]
    public void FromMagnet_of_a_hybrid_link_reads_both_topics()
    {
        //Arrange
        var magnet = $"magnet:?xt=urn:btih:{HybridV1Hash}&xt=urn:btmh:1220{HybridV2Hash}";

        //Act
        var hashes = TorrentInfoHashReader.FromMagnet(magnet);

        //Assert
        Assert.Equal(new TorrentInfoHashes(HybridV1Hash, HybridV2Hash), hashes);
    }

    [Fact]
    public void FromMagnet_naming_two_different_v1_hashes_leaves_v1_unknown()
    {
        //Arrange
        // Which of two torrents the link means is not the app's to pick.
        var magnet = $"magnet:?xt.1=urn:btih:{V1Hash}&xt.2=urn:btih:{HybridV1Hash}&xt=urn:btmh:1220{V2Hash}";

        //Act
        var hashes = TorrentInfoHashReader.FromMagnet(magnet);

        //Assert
        Assert.Equal(new TorrentInfoHashes(null, V2Hash), hashes);
    }

    [Fact]
    public void FromMagnet_naming_one_v1_hash_twice_keeps_it()
    {
        //Arrange
        var magnet = $"magnet:?xt=urn:btih:{V1Hash}&xt.1=urn:btih:{V1HashBase32}";

        //Act
        var hashes = TorrentInfoHashReader.FromMagnet(magnet);

        //Assert
        Assert.Equal(V1Hash, hashes.V1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://tracker.test/download/1")]
    [InlineData("magnet:")]
    [InlineData("magnet:?dn=x.mkv")]
    [InlineData("magnet:?xt=urn:btih:" + "72d6069eff259a0800f7afdb2500d5de591a9f0")]
    [InlineData("magnet:?xt=urn:btih:" + "72d6069eff259a0800f7afdb2500d5de591a9f011")]
    [InlineData("magnet:?xt=urn:btih:" + "72d6069eff259a0800f7afdb2500d5de591a9f0g")]
    [InlineData("magnet:?xt=urn:btih:OLLANHX7EWNAQAHXV7NSKAGV3ZMRVHY1")]
    [InlineData("magnet:?xt=urn:btmh:1114" + V2Hash)]
    [InlineData("magnet:?xt=urn:btmh:1220" + "3d8c42fa5353329627f1a2afcba8bc04c6b5db714d3b07577c9624e6a4396bc")]
    [InlineData("magnet:?xtx=urn:btih:" + V1Hash)]
    [InlineData("magnet:?xt.a=urn:btih:" + V1Hash)]
    [InlineData("magnet:?xt=urn:sha1:" + V1Hash)]
    // Fullwidth digits are digits to char.IsDigit, never to a hash.
    [InlineData("magnet:?xt=urn:btih:７２d6069eff259a0800f7afdb2500d5de591a9f")]
    public void FromMagnet_without_a_topic_the_app_can_read_is_no_hash(string magnet)
    {
        //Arrange

        //Act
        var hashes = TorrentInfoHashReader.FromMagnet(magnet);

        //Assert
        Assert.Equal(TorrentInfoHashes.None, hashes);
    }

    [Fact]
    public void Read_takes_each_payload_kind_by_what_it_carries()
    {
        //Arrange
        var torrentFile = new ReleasePayload(ReleasePayloadKind.TorrentFile, Torrent(V1Info), null);
        var magnet = new ReleasePayload(ReleasePayloadKind.Magnet, null, $"magnet:?xt=urn:btmh:1220{V2Hash}");
        var nzb = new ReleasePayload(ReleasePayloadKind.Nzb, "<nzb/>"u8.ToArray(), null);
        var torrentWithoutBytes = new ReleasePayload(ReleasePayloadKind.TorrentFile, null, $"magnet:?xt=urn:btih:{V1Hash}");

        //Act
        var hashes = new[] { torrentFile, magnet, nzb, torrentWithoutBytes }.Select(TorrentInfoHashReader.Read).ToList();

        //Assert
        Assert.Equal(
            [new TorrentInfoHashes(V1Hash, null), new TorrentInfoHashes(null, V2Hash), TorrentInfoHashes.None, TorrentInfoHashes.None],
            hashes);
    }

    [Fact]
    public void Contains_matches_either_hash_in_any_letter_case_and_never_a_missing_one()
    {
        //Arrange
        var hashes = new TorrentInfoHashes(HybridV1Hash, null);

        //Act
        var matches = (hashes.Contains(HybridV1Hash.ToUpperInvariant()), hashes.Contains(HybridV2Hash), hashes.Contains(null));

        //Assert
        Assert.Equal((true, false, false), matches);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static byte[] Torrent(string info) => Encoding.ASCII.GetBytes("d8:announce19:http://tracker.test4:info" + info + "e");
}
