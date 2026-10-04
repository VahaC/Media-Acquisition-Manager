using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>
/// MAM-32 — a series' seasons as the Library page lists them: each season of the given positions with how
/// many different episode works it holds.
/// </summary>
/// <remarks>
/// Regular seasons in order, then the specials (season 0, where Jellyfin files them), then the episodes
/// the library gives no season: the order Jellyfin's own pages show. An episode two positions put in two
/// seasons counts in both, as it is listed in both.
/// </remarks>
public static class LibrarySeasonCounts
{
    /// <summary>
    /// The seasons of each of <paramref name="seriesIds"/> among <paramref name="positions"/> — already
    /// narrowed to the episodes a list keeps — in one query; a series with none has an empty list.
    /// </summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<LibrarySeasonDto>>> ReadAsync(
        IQueryable<MediaEpisodePositionEntity> positions, IReadOnlyCollection<Guid> seriesIds, CancellationToken cancellationToken)
    {
        var ids = seriesIds.ToList();
        var rows = await positions
            .Where(position => position.SeriesWorkId != null && ids.Contains(position.SeriesWorkId.Value))
            .Select(position => new { SeriesWorkId = position.SeriesWorkId!.Value, position.SeasonNumber, position.MediaWorkId })
            .Distinct()
            .ToListAsync(cancellationToken);
        var bySeries = rows.ToLookup(row => row.SeriesWorkId);
        return ids.Distinct().ToDictionary(
            id => id,
            id => (IReadOnlyList<LibrarySeasonDto>)
            [
                .. bySeries[id]
                    .GroupBy(row => row.SeasonNumber)
                    .OrderBy(season => season.Key is null ? 2 : season.Key == 0 ? 1 : 0)
                    .ThenBy(season => season.Key)
                    .Select(season => new LibrarySeasonDto(season.Key, season.Count())),
            ]);
    }
}
