using System.Globalization;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// R3 — the confirmation's words, built once for the dialog, the refusal, the job's row and the audit row: "This release
/// is Alien (1979)", "This release is Alien" without a year; a series with its year when known and what the release
/// claims of it — ", season 2", ", seasons 1–5", ", season 2, episodes 1–5", ", season 2, episode 3"; and, for two works
/// that would read alike, what tells them apart — their libraries, the copy each lists first, or their place.
/// </summary>
/// <remarks>
/// MAM-47 (ADR 0042, design §5.5) — the claim the owner confirms, as a message of the <c>replacements.confirm</c> keys:
/// which work, which seasons and episodes, which twin. What he confirmed is kept exactly; the words are rendered in the
/// reader's language, and the audit row names it by <see cref="Audit.ReplacementConfirmationLabel"/>. A work with no title
/// and the libraries' tail are keys of their own (design §1.3: a fragment that fills the sentence is a shape of it), and
/// the libraries can't be a wrapper around the claim as the other twins are: “{{confirmation}}” in “{{libraries}}” would
/// read a held torrent's “{{torrent}}” in “{{client}}” too, and the converter couldn't tell the two apart.
/// </remarks>
public static class ReplacementConfirmation
{
    /// <summary>
    /// A work as the sentences name it — "Alien (1979)", "Alien" without a year: its title and year as data, the way the
    /// Library names a work in every language —; <c>null</c> for a work with no title, which a sentence names by a key of
    /// its own words.
    /// </summary>
    public static string? NameOf(string? title, int? year) =>
        title is null ? null
        : year is { } known ? string.Create(CultureInfo.InvariantCulture, $"{title} ({known})")
        : title;

    /// <summary>
    /// The confirmation of the work <paramref name="title"/> of <paramref name="year"/>, with what the release claims of a
    /// series (<paramref name="claimed"/>; <c>null</c> for a film, or a series whose title claims no season), and the
    /// <paramref name="libraries"/> holding its copies when they tell it from a twin.
    /// </summary>
    /// <remarks>MAM-47 — a work with no title is named by its keys' words alone, as the planner's sentences name it: its
    /// year, which only "a work with no title (2005)" carried, is left out rather than doubling every key for a work the
    /// library knows by its ids and year but by no title.</remarks>
    public static Message Of(string? title, int? year, ClaimedEpisodes? claimed, IReadOnlyList<string>? libraries = null)
    {
        var name = NameOf(title, year);
        var claim = ClaimOf(claimed);
        var inLibraries = libraries is { Count: > 0 };
        List<(string Name, MessageValue Value)> values = [];
        if (name is not null)
            values.Add(("work", name));
        switch (claim)
        {
            case Claim.Season or Claim.SeasonEpisode or Claim.SeasonEpisodes:
                values.Add(("season", claimed!.FirstSeason));
                break;
            case Claim.Seasons or Claim.SeasonsEpisode or Claim.SeasonsEpisodes:
                values.Add(("firstSeason", claimed!.FirstSeason));
                values.Add(("lastSeason", claimed.LastSeason));
                break;
        }

        switch (claim)
        {
            case Claim.SeasonEpisode or Claim.SeasonsEpisode:
                values.Add(("episode", claimed!.FirstEpisode!.Value));
                break;
            case Claim.SeasonEpisodes or Claim.SeasonsEpisodes:
                values.Add(("firstEpisode", claimed!.FirstEpisode!.Value));
                values.Add(("lastEpisode", claimed.LastEpisode!.Value));
                break;
        }

        if (inLibraries)
            values.Add(("libraries", new ListValue(libraries!.Select(library => (MessageValue)library))));
        return Message.Of(KeyOf(claim, name is not null, inLibraries), [.. values]);
    }

    /// <summary>Twins whose libraries read alike told apart by the name of the copy each lists first: "… (the copy
    /// “Halloween.2018.1080p.mkv”)".</summary>
    public static Message TheCopy(Message confirmation, string name) =>
        Message.Of("replacements.confirm.the-copy", ("confirmation", confirmation), ("name", name));

    /// <summary>Twins nothing else tells apart, by their place among them in the dialog's order: "… (1 of 2)".</summary>
    public static Message Place(Message confirmation, int place, int total) =>
        Message.Of("replacements.confirm.place", ("confirmation", confirmation), ("place", place), ("total", total));

    private static Claim ClaimOf(ClaimedEpisodes? claimed) => claimed switch
    {
        null => Claim.Work,
        { FirstEpisode: { } first, LastEpisode: { } last } when claimed.FirstSeason == claimed.LastSeason =>
            first == last ? Claim.SeasonEpisode : Claim.SeasonEpisodes,
        { FirstEpisode: { } first, LastEpisode: { } last } => first == last ? Claim.SeasonsEpisode : Claim.SeasonsEpisodes,
        _ when claimed.FirstSeason == claimed.LastSeason => Claim.Season,
        _ => Claim.Seasons,
    };

    // One whole literal per key (design §1.3), so the key check sees every one.
    private static string KeyOf(Claim claim, bool titled, bool inLibraries) => (claim, titled, inLibraries) switch
    {
        (Claim.Work, true, false) => "replacements.confirm.work",
        (Claim.Season, true, false) => "replacements.confirm.work-season",
        (Claim.Seasons, true, false) => "replacements.confirm.work-seasons",
        (Claim.SeasonEpisode, true, false) => "replacements.confirm.work-season-episode",
        (Claim.SeasonEpisodes, true, false) => "replacements.confirm.work-season-episodes",
        (Claim.SeasonsEpisode, true, false) => "replacements.confirm.work-seasons-episode",
        (Claim.SeasonsEpisodes, true, false) => "replacements.confirm.work-seasons-episodes",
        (Claim.Work, false, false) => "replacements.confirm.untitled",
        (Claim.Season, false, false) => "replacements.confirm.untitled-season",
        (Claim.Seasons, false, false) => "replacements.confirm.untitled-seasons",
        (Claim.SeasonEpisode, false, false) => "replacements.confirm.untitled-season-episode",
        (Claim.SeasonEpisodes, false, false) => "replacements.confirm.untitled-season-episodes",
        (Claim.SeasonsEpisode, false, false) => "replacements.confirm.untitled-seasons-episode",
        (Claim.SeasonsEpisodes, false, false) => "replacements.confirm.untitled-seasons-episodes",
        (Claim.Work, true, true) => "replacements.confirm.work-in-libraries",
        (Claim.Season, true, true) => "replacements.confirm.work-season-in-libraries",
        (Claim.Seasons, true, true) => "replacements.confirm.work-seasons-in-libraries",
        (Claim.SeasonEpisode, true, true) => "replacements.confirm.work-season-episode-in-libraries",
        (Claim.SeasonEpisodes, true, true) => "replacements.confirm.work-season-episodes-in-libraries",
        (Claim.SeasonsEpisode, true, true) => "replacements.confirm.work-seasons-episode-in-libraries",
        (Claim.SeasonsEpisodes, true, true) => "replacements.confirm.work-seasons-episodes-in-libraries",
        (Claim.Work, false, true) => "replacements.confirm.untitled-in-libraries",
        (Claim.Season, false, true) => "replacements.confirm.untitled-season-in-libraries",
        (Claim.Seasons, false, true) => "replacements.confirm.untitled-seasons-in-libraries",
        (Claim.SeasonEpisode, false, true) => "replacements.confirm.untitled-season-episode-in-libraries",
        (Claim.SeasonEpisodes, false, true) => "replacements.confirm.untitled-season-episodes-in-libraries",
        (Claim.SeasonsEpisode, false, true) => "replacements.confirm.untitled-seasons-episode-in-libraries",
        _ => "replacements.confirm.untitled-seasons-episodes-in-libraries",
    };

    /// <summary>What the release claims of the work, as the words say it.</summary>
    private enum Claim
    {
        /// <summary>A film, or a series whose title claims no season: the work alone.</summary>
        Work = 1,

        /// <summary>One season.</summary>
        Season = 2,

        /// <summary>A run of seasons.</summary>
        Seasons = 3,

        /// <summary>One episode of one season.</summary>
        SeasonEpisode = 4,

        /// <summary>A run of episodes of one season.</summary>
        SeasonEpisodes = 5,

        /// <summary>One episode number over a run of seasons.</summary>
        SeasonsEpisode = 6,

        /// <summary>A run of episode numbers over a run of seasons.</summary>
        SeasonsEpisodes = 7,
    }
}
