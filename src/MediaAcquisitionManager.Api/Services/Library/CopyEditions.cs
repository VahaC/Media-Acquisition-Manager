using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// MAM-37 — the edition words a copy of a work holds (<see cref="EditionLabels"/>): its file's name and its version's
/// label, with the work's own titles and its series' taken out first, so a title is never read as an edition
/// ("Uncut.Gems.2019.mkv" is no Uncut cut).
/// </summary>
/// <remarks>
/// MAM-24 — moved here unchanged from <c>LibraryDuplicateService</c>, the words' first reader, when the Download
/// dialog came to show them beside each old copy: one rule, so the Duplicates page and the dialog never read one copy's
/// edition differently.
/// </remarks>
public static class CopyEditions
{
    /// <summary>
    /// The edition words of a copy named <paramref name="servicePath"/> whose versions are labelled
    /// <paramref name="editionLabels"/>, its holders' <paramref name="ownTitles"/> taken out: each word once, in the
    /// enum's order.
    /// </summary>
    public static IReadOnlyList<MediaEdition> Of(string servicePath, IEnumerable<string?> editionLabels, IReadOnlyCollection<string> ownTitles) =>
        [.. EditionLabels.Read(FileNameOf(servicePath), ownTitles)
            .Concat(editionLabels.SelectMany(label => EditionLabels.Read(label, ownTitles)))
            .Distinct()
            .Order()];

    /// <summary>The titles a work goes by, every source's, and its shows': the words a name of it holds that are no
    /// edition.</summary>
    public static IEnumerable<string> TitlesOfWork(
        Guid workId, ILookup<Guid, MediaWorkTitleEntity> titles, ILookup<Guid, MediaEpisodePositionEntity> positions) =>
        titles[workId]
            .Concat(positions[workId].Select(position => position.SeriesWorkId).OfType<Guid>().SelectMany(seriesId => titles[seriesId]))
            .Select(title => title.Text);

    /// <summary>The last name of a path in either style: where a file's name says its edition, an episode's segment and
    /// its year.</summary>
    public static string FileNameOf(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
}
