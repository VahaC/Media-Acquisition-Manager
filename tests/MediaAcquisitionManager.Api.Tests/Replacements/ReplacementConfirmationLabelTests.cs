using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-47 (ADR 0042, design §5.5; Vasyl, 2026-10-19, Q1) — the owner's consent words as the claim they say, in today's
// English to the letter, and the audit row's label of that claim in no language: what the scheduling writes, and what the
// startup converter writes for an older build's row, so an old row and a new one read alike. The dialog, the refusal and
// the rows over HTTP are pinned in ReplacementOptions*HttpTests and ReplacementScheduleHttpTests.
public sealed class ReplacementConfirmationLabelTests
{
    public static TheoryData<string?, int?, int?, int?, int?, int?, string, string> Claims => new()
    {
        { "Alien", 1979, null, null, null, null, "This release is Alien (1979)", "Alien (1979)" },
        { "Alien", null, null, null, null, null, "This release is Alien", "Alien" },
        { "Doctor Who", 2005, 2, 2, null, null, "This release is Doctor Who (2005), season 2", "Doctor Who (2005) S02" },
        { "Doctor Who", 2005, 1, 3, null, null, "This release is Doctor Who (2005), seasons 1–3", "Doctor Who (2005) S01–S03" },
        { "Doctor Who", 2005, 2, 2, 3, 3, "This release is Doctor Who (2005), season 2, episode 3", "Doctor Who (2005) S02E03" },
        { "Doctor Who", 2005, 2, 2, 1, 5, "This release is Doctor Who (2005), season 2, episodes 1–5", "Doctor Who (2005) S02E01–E05" },
        { "Doctor Who", 2005, 1, 3, 5, 5, "This release is Doctor Who (2005), seasons 1–3, episode 5", "Doctor Who (2005) S01–S03E05" },
        { "Doctor Who", 2005, 1, 3, 1, 5, "This release is Doctor Who (2005), seasons 1–3, episodes 1–5", "Doctor Who (2005) S01–S03E01–E05" },
        { "Doctor Who", 2005, 12, 12, 101, 101, "This release is Doctor Who (2005), season 12, episode 101", "Doctor Who (2005) S12E101" },
        { null, 2005, 1, 1, null, null, "This release is a work with no title, season 1", "— S01" },
        { null, null, null, null, null, null, "This release is a work with no title", "—" },
    };

    [Theory]
    [MemberData(nameof(Claims))]
    public void Of_says_the_claim_in_todays_english_and_its_label_in_no_language(
        string? title, int? year, int? firstSeason, int? lastSeason, int? firstEpisode, int? lastEpisode, string english, string label)
    {
        //Arrange
        var claimed = firstSeason is { } first ? new ClaimedEpisodes(first, lastSeason!.Value, firstEpisode, lastEpisode) : null;

        //Act
        var confirmation = ReplacementConfirmation.Of(title, year, claimed);

        //Assert
        Assert.Equal((english, label), (TestTexts.En(confirmation), ReplacementConfirmationLabel.Of(confirmation)));
    }

    [Fact]
    public void Of_twins_keeps_what_tells_them_apart_in_the_words_and_in_the_label()
    {
        //Arrange
        var inLibraries = ReplacementConfirmation.Of("Alien", 1979, null, ["Фільми", "Kids"]);
        var halloween = ReplacementConfirmation.Of("Halloween", 2018, null, ["Фільми"]);
        var untitled = ReplacementConfirmation.Of(null, null, new ClaimedEpisodes(2, 2, null, null), ["Серіали"]);

        //Act
        Message[] twins =
        [
            inLibraries,
            ReplacementConfirmation.TheCopy(halloween, "Halloween.2018.1080p.mkv"),
            ReplacementConfirmation.Place(halloween, 1, 2),
            untitled,
        ];

        //Assert
        Assert.Equal(
            [
                ("This release is Alien (1979) in “Фільми, Kids”", "Alien (1979) · Фільми, Kids"),
                ("This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.1080p.mkv”)", "Halloween (2018) · Фільми · Halloween.2018.1080p.mkv"),
                ("This release is Halloween (2018) in “Фільми” (1 of 2)", "Halloween (2018) · Фільми · 1/2"),
                ("This release is a work with no title, season 2 in “Серіали”", "— S02 · Серіали"),
            ],
            twins.Select(twin => (TestTexts.En(twin)!, ReplacementConfirmationLabel.Of(twin))));
    }

    [Theory]
    [InlineData("This release is Alien (1979)", 1000, "Alien (1979)")]
    [InlineData("This release is Doctor Who (2005), season 2, episodes 1–5", 1000, "Doctor Who (2005) S02E01–E05")]
    [InlineData("This release is Alien (1979) in “Кіно”", 1000, "Alien (1979) · Кіно")]
    [InlineData("This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.1080p.mkv”)", 200, "Halloween (2018) · Фільми · Halloween.2018.1080p.mkv")]
    [InlineData("This release is Halloween (2018) (2 of 2)", 200, "Halloween (2018) · 2/2")]
    public void Of_an_older_builds_words_read_back_as_their_claim_gives_the_same_label(string stored, int columnLength, string label)
    {
        //Arrange

        //Act
        // The startup converter reads the consent words through the keys this prefix names: they end with no full stop.
        var readings = MessageParser.Embedded.Parse(stored, columnLength, keyPrefix: "replacements.confirm.");

        //Assert
        var reading = Assert.Single(readings);
        Assert.Equal((stored, label), (TestTexts.En(reading.Message), ReplacementConfirmationLabel.Of(reading.Message)));
    }

    [Fact]
    public void ReplacementConfirmation_of_an_audit_target_cuts_a_long_label_to_the_column()
    {
        //Arrange
        var job = new ReplacementJobEntity();
        var confirmation = ReplacementConfirmation.Of(new string('a', 300), 1979, null);

        //Act
        var target = AuditTarget.ReplacementConfirmation(job, confirmation);

        //Assert
        Assert.Equal(
            (AuditTargetKind.ReplacementJob, job.Id, string.Concat(new string('a', AuditTarget.TargetNameMaximumLength - 1), "…")),
            (target.Kind, target.Id, target.Name));
    }
}
