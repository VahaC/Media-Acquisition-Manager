using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26 — how far a write over a replacement's names got, as every sentence of the mover, its recovery, Restore and
// Delete for good says it: the noun agreeing with the total, the verb and its pronoun with the count, and no count at all
// when nothing got anywhere. The sentences themselves are pinned whole in the HTTP tests of each. MAM-47 (ADR 0042) — a
// message of the tally keys, its English rendered strict: the count of names written picks the verb's form, and the total
// is a counted noun of its own, nested, so every language agrees both as its grammar wants.
public sealed class ReplacementNameTallyTests
{
    [Theory]
    [InlineData(1, 1, "1 of 1 name is in the quarantine")]
    [InlineData(1, 3, "1 of 3 names is in the quarantine")]
    [InlineData(2, 3, "2 of 3 names are in the quarantine")]
    [InlineData(0, 3, null)]
    public void InQuarantine_agrees_with_the_total_and_the_count_and_says_nothing_of_none(int count, int total, string? expected)
    {
        //Arrange

        //Act
        var clause = ReplacementNameTally.InQuarantine(count, total);

        //Assert
        Assert.Equal(expected, TestTexts.En(clause));
    }

    [Theory]
    [InlineData(1, 1, "1 of 1 name is back where it was")]
    [InlineData(1, 3, "1 of 3 names is back where it was")]
    [InlineData(2, 3, "2 of 3 names are back where they were")]
    [InlineData(0, 2, null)]
    public void BackWhereTheyWere_agrees_its_pronoun_with_the_count_too(int count, int total, string? expected)
    {
        //Arrange

        //Act
        var clause = ReplacementNameTally.BackWhereTheyWere(count, total);

        //Assert
        Assert.Equal(expected, TestTexts.En(clause));
    }

    [Theory]
    [InlineData(1, 2, "1 of 2 names is deleted")]
    [InlineData(2, 2, "2 of 2 names are deleted")]
    [InlineData(0, 2, null)]
    public void Deleted_agrees_with_the_total_and_the_count_and_says_nothing_of_none(int count, int total, string? expected)
    {
        //Arrange

        //Act
        var clause = ReplacementNameTally.Deleted(count, total);

        //Assert
        Assert.Equal(expected, TestTexts.En(clause));
    }

    [Fact]
    public void InQuarantine_counts_the_names_written_and_nests_the_total_as_a_counted_noun_of_its_own()
    {
        //Arrange
        var names = Message.Of("replacements.quarantine.tally.names", ("count", 3));

        //Act
        var clause = ReplacementNameTally.InQuarantine(2, 3);

        //Assert
        Assert.Equal(Message.Of("replacements.quarantine.tally.in-quarantine", ("count", 2), ("names", names)), clause);
    }

    [Fact]
    public void BackWhereTheyWere_and_Deleted_say_each_write_by_its_own_key()
    {
        //Arrange
        var names = Message.Of("replacements.quarantine.tally.names", ("count", 2));

        //Act
        var back = ReplacementNameTally.BackWhereTheyWere(1, 2);
        var deleted = ReplacementNameTally.Deleted(1, 2);

        //Assert
        Assert.Equal(
            (Message.Of("replacements.quarantine.tally.back", ("count", 1), ("names", names)),
                Message.Of("replacements.quarantine.tally.deleted", ("count", 1), ("names", names))),
            (back, deleted));
    }

    // MAM-47 — the joiners are gone with the last sentence written in code (a removal's, WP9 part 2): a sentence nests the
    // tally in a "-tally" key of its own, after a colon or as a sentence of its own, and says nothing of it when none is.

    [Theory]
    [InlineData(1, 4, "The app stopped while moving this copy: 1 of 4 names is in the quarantine.")]
    [InlineData(0, 4, "The app stopped while moving this copy.")]
    public void A_tally_key_ends_its_sentence_with_the_clause_after_a_colon_and_the_plain_key_with_a_full_stop(int count, int total, string expected)
    {
        //Arrange
        var clause = ReplacementNameTally.InQuarantine(count, total);

        //Act
        var sentence = clause is null
            ? Message.Of("replacements.quarantine.cut.moving-removal")
            : Message.Of("replacements.quarantine.cut.moving-removal-tally", ("tally", clause));

        //Assert
        Assert.Equal(expected, TestTexts.En(sentence));
    }

    [Theory]
    [InlineData(1, 2, "“a.srt” couldn't be deleted: The file system gave no reason. 1 of 2 names is deleted.")]
    [InlineData(0, 2, "“a.srt” couldn't be deleted: The file system gave no reason.")]
    public void A_tally_key_adds_the_clause_as_a_sentence_of_its_own_after_a_nested_one(int count, int total, string expected)
    {
        //Arrange
        var clause = ReplacementNameTally.Deleted(count, total);
        var error = Message.Of("replacements.quarantine.no-reason");

        //Act
        var sentence = clause is null
            ? Message.Of("replacements.quarantine.delete.failed", ("file", "a.srt"), ("error", error))
            : Message.Of("replacements.quarantine.delete.failed-tally", ("file", "a.srt"), ("error", error), ("tally", clause));

        //Assert
        Assert.Equal(expected, TestTexts.En(sentence));
    }
}
