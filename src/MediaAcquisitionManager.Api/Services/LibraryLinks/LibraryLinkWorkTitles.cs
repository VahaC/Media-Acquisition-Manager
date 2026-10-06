using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — what a library work's titles say to the TMDb suggestions: the titles cleaned of what a file name adds around
/// them, and its year. One rule for the run that searches by them and the reader that ranks by them, so a work is asked
/// about by the very titles its candidates are then compared with.
/// </summary>
internal static class LibraryLinkWorkTitles
{
    /// <summary>
    /// The work's titles cleaned (<see cref="SeedTitleCleaning"/>: "Landing Stripling" of "120. Landing Stripling",
    /// "Planet Earth" of "Planet_Earth_E08 - Jungles"), displayed ones first, each in title order, one per title key; a
    /// title nothing is left of is dropped.
    /// </summary>
    public static IReadOnlyList<string> Cleaned(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        [.. CleanedWithSources(titles).Select(title => title.Cleaned)];

    /// <summary>
    /// The work's year: the one its card shows (the displayed title's, else the original's — LibraryMapper's rule), else
    /// the first year a bracketed year or span of those titles names ("(2008-2015)" of a series named after its folder,
    /// the MAM-33 rule); <c>null</c> when none says.
    /// </summary>
    public static int? YearOf(IReadOnlyList<MediaWorkTitleEntity> titles)
    {
        var display = LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Display);
        var original = LibraryMapper.FirstInTitleOrder(titles, MediaTitleKind.Original);
        return display?.Year
            ?? original?.Year
            ?? (display is null ? null : SeedTitleCleaning.BracketedYear(display.Text))
            ?? (original is null ? null : SeedTitleCleaning.BracketedYear(original.Text));
    }

    /// <summary>
    /// What the dialog's free search starts from, so its first Search finds what the run's would: the first of the cleaned
    /// titles (<see cref="Cleaned"/>, the run's first query) — never the library's raw title, which TMDb finds nothing by —
    /// and the year that title names in brackets, else the work's year (<see cref="YearOf"/>). The bracket wins: a series
    /// named after its folder, "The Penguins Of Madagascar (Season 1-3) (2008-2015) …", names its first year there (2008,
    /// which TMDb's year filter finds it by), while the library gives it its last (2015, which finds nothing). Both
    /// <c>null</c> when no title is left; the year alone when the titles name one.
    /// </summary>
    public static (string? Title, int? Year) SearchOf(IReadOnlyList<MediaWorkTitleEntity> titles)
    {
        var first = CleanedWithSources(titles).FirstOrDefault();
        return first.Source is null
            ? (null, YearOf(titles))
            : (first.Cleaned, SeedTitleCleaning.BracketedYear(first.Source.Text) ?? YearOf(titles));
    }

    /// <summary>The cleaned titles in <see cref="Cleaned"/>'s order, each with the title it was cleaned of.</summary>
    private static IEnumerable<(MediaWorkTitleEntity Source, string Cleaned)> CleanedWithSources(IReadOnlyList<MediaWorkTitleEntity> titles) =>
        titles
            .OrderBy(title => title.Kind == MediaTitleKind.Display ? 0 : 1)
            .ThenBy(title => title.Text, LibraryTitleOrder.Comparer)
            .ThenBy(title => title.Id)
            .Select(title => (Source: title, Cleaned: SeedTitleCleaning.Clean(title.Text)))
            .Where(title => TitleMatchKey.For(title.Cleaned).Length > 0)
            .DistinctBy(title => TitleMatchKey.For(title.Cleaned));
}
