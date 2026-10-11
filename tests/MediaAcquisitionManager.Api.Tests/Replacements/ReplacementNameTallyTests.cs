using MediaAcquisitionManager.Api.Services.Replacements;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26 — how far a write over a replacement's names got, as every sentence of the mover, its recovery, Restore and
// Delete for good says it: the noun agreeing with the total, the verb and its pronoun with the count, and no count at all
// when nothing got anywhere. The sentences themselves are pinned whole in the HTTP tests of each.
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
        Assert.Equal(expected, clause);
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
        Assert.Equal(expected, clause);
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
        Assert.Equal(expected, clause);
    }

    [Theory]
    [InlineData("1 of 4 names is in the quarantine", "The app stopped while moving the old copies: 1 of 4 names is in the quarantine.")]
    [InlineData(null, "The app stopped while moving the old copies.")]
    public void AfterColon_ends_the_lead_with_the_clause_or_a_full_stop_alone(string? clause, string expected)
    {
        //Arrange

        //Act
        var sentence = ReplacementNameTally.AfterColon("The app stopped while moving the old copies", clause);

        //Assert
        Assert.Equal(expected, sentence);
    }

    [Theory]
    [InlineData("1 of 2 names is deleted", "“a.srt” couldn't be deleted: The disk said no. 1 of 2 names is deleted.")]
    [InlineData(null, "“a.srt” couldn't be deleted: The disk said no.")]
    public void AfterSentence_adds_the_clause_as_a_sentence_of_its_own_or_nothing(string? clause, string expected)
    {
        //Arrange

        //Act
        var sentence = ReplacementNameTally.AfterSentence("“a.srt” couldn't be deleted: The disk said no.", clause);

        //Assert
        Assert.Equal(expected, sentence);
    }
}
