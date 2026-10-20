using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-47 (ADR 0042) — check 6's sentences as messages: one key per wording (a replacement's old copy and Retry, a removal's
// copy and Remove again) and per one torrent or several, and the clients the read left uncertain said one after another,
// each sentence nesting the next — a whole sentence per shape, never a wording that opens with a value to glue them, which
// the startup converter would try on every stored text. Their English, pinned whole here, is what the HTTP tests of the
// mover and of Remove read in their answers too.
public sealed class QuarantineHoldingCheckTests
{
    private static readonly ReplacementClientReading NotRead = new(
        "qBittorrent", ReplacementClientCheck.NotRead, Message.Of("replacements.holdings.switched-off", ("client", "qBittorrent")), 0);

    private static readonly ReplacementClientReading ReadInPart = new("seedbox", ReplacementClientCheck.Partial, null, UnplacedTorrents: 2, OutsideTorrents: 1);

    [Fact]
    public void UncertainReason_says_each_clients_sentences_in_order_each_nesting_the_next()
    {
        //Arrange

        //Act
        var reason = QuarantineHoldingCheck.UncertainReason([NotRead, ReadInPart], QuarantineWording.Replacement);

        //Assert
        Assert.Equal(
            "The app couldn't tell whether a torrent in “qBittorrent” holds the old copy: “qBittorrent” is switched off. "
            + "The app couldn't place 2 of “seedbox”'s torrents in its folders, so it can't tell whether one of them holds the old copy. "
            + "“seedbox”'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether that torrent holds "
            + "the old copy: check those mappings under Integrations, then press Retry.",
            TestTexts.En(reason));
        Assert.Equal(
            [
                "replacements.quarantine.uncertain.not-read-replacement-more",
                "replacements.quarantine.uncertain.unplaced-replacement-more",
                "replacements.quarantine.uncertain.outside-replacement",
            ],
            Chain(reason));
    }

    [Fact]
    public void UncertainReason_stored_in_english_by_an_older_build_reads_back_as_the_same_chain()
    {
        //Arrange
        // The words a 0.4.0 job's reason holds for two clients, as the startup converter reads them (design §6.3).
        var reason = QuarantineHoldingCheck.UncertainReason([NotRead, ReadInPart], QuarantineWording.Replacement);
        var english = TestTexts.En(reason);

        //Act
        var readings = MessageParser.Embedded.Parse(english, MessageJson.ColumnLength);

        //Assert
        Assert.Equal(reason, Assert.Single(readings).Message);
    }

    [Fact]
    public void UncertainReason_of_a_removal_names_this_copy_and_remove_again()
    {
        //Arrange

        //Act
        var reason = QuarantineHoldingCheck.UncertainReason([ReadInPart], QuarantineWording.Removal);

        //Assert
        Assert.Equal(
            "The app couldn't place 2 of “seedbox”'s torrents in its folders, so it can't tell whether one of them holds this copy. "
            + "“seedbox”'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether that torrent holds "
            + "this copy: check those mappings under Integrations, then press Remove again.",
            TestTexts.En(reason));
        Assert.Equal(["replacements.quarantine.uncertain.unplaced-removal-more", "replacements.quarantine.uncertain.outside-removal"], Chain(reason));
    }

    [Theory]
    [InlineData(QuarantineWording.Replacement, 1, "replacements.quarantine.held.by-torrent-replacement",
        "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Retry.")]
    [InlineData(QuarantineWording.Replacement, 2, "replacements.quarantine.held.by-torrents-replacement",
        "Still held by the torrents “Alien” in “qBittorrent” and “Alien” in “seedbox”: remove those torrents there without their files, then press Retry.")]
    [InlineData(QuarantineWording.Removal, 1, "replacements.quarantine.held.by-torrent-removal",
        "Still held by the torrent “Alien” in “qBittorrent”: remove that torrent there without its files, then press Remove again.")]
    [InlineData(QuarantineWording.Removal, 2, "replacements.quarantine.held.by-torrents-removal",
        "Still held by the torrents “Alien” in “qBittorrent” and “Alien” in “seedbox”: remove those torrents there without their files, then press Remove again.")]
    public void HeldReason_names_one_torrent_or_several_by_a_key_of_the_wordings_own(QuarantineWording wording, int torrents, string key, string expected)
    {
        //Arrange
        ReplacementHold[] holds = [new("qBittorrent", "aa11", "Alien", false), new("seedbox", "bb22", "Alien", false)];

        //Act
        var reason = QuarantineHoldingCheck.HeldReason(holds[..torrents], wording);

        //Assert
        Assert.Equal((key, expected), (reason.Key, TestTexts.En(reason)));
    }

    [Fact]
    public void HeldReason_names_a_torrent_held_twice_by_its_client_once()
    {
        //Arrange
        // Two files of one torrent reaching the copy: one hold, told apart by client and id, never by name.
        List<ReplacementHold> holds = [new("qBittorrent", "aa11", "Alien", false), new("qBittorrent", "aa11", "Alien", false)];

        //Act
        var reason = QuarantineHoldingCheck.CompanionHeldReason(holds);

        //Assert
        Assert.Equal(
            ("replacements.quarantine.held.companion-by-torrent", "Held by the torrent “Alien” in “qBittorrent”, so it stays where it is."),
            (reason.Key, TestTexts.En(reason)));
    }

    // The keys of a reason and of every sentence it nests as its "more", in order.
    private static List<string> Chain(Message reason)
    {
        List<string> keys = [];
        for (Message? sentence = reason; sentence is not null; sentence = sentence.Values.GetValueOrDefault("more") as Message)
            keys.Add(sentence.Key);
        return keys;
    }
}
