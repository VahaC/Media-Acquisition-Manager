using System.Globalization;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>
/// Every row the library syncs store (MAM-8) — works, titles, identifiers, versions, files and libraries —
/// read at once, so a test asserts what a sync stored, or that a failed one stored nothing, row by row.
/// </summary>
/// <param name="Works">Every work.</param>
/// <param name="Titles">Every title.</param>
/// <param name="Identifiers">Every registry id.</param>
/// <param name="Versions">Every version.</param>
/// <param name="Files">Every file.</param>
/// <param name="Locations">Every library of a media library integration.</param>
public sealed record StoredLibrarySnapshot(
    IReadOnlyList<MediaWorkEntity> Works,
    IReadOnlyList<MediaWorkTitleEntity> Titles,
    IReadOnlyList<MediaWorkIdentifierEntity> Identifiers,
    IReadOnlyList<MediaVersionEntity> Versions,
    IReadOnlyList<PhysicalFileEntity> Files,
    IReadOnlyList<LibraryLocationEntity> Locations)
{
    /// <summary>Every snapshot row in <paramref name="db"/>, each table ordered by id.</summary>
    public static async Task<StoredLibrarySnapshot> ReadAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        // Ordered in memory: a Guid's order in SQLite is its text's, and a test compares two reads of the
        // same rows, never an order it predicts.
        return new StoredLibrarySnapshot(
            [.. (await db.MediaWorks.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(row => row.Id)],
            [.. (await db.MediaWorkTitles.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(row => row.Id)],
            [.. (await db.MediaWorkIdentifiers.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(row => row.Id)],
            [.. (await db.MediaVersions.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(row => row.Id)],
            [.. (await db.PhysicalFiles.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(row => row.Id)],
            [.. (await db.LibraryLocations.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(row => row.Id)]);
    }

    /// <summary>
    /// Every row as one line with every column a sync writes, first-seen and last-seen times included
    /// (<see cref="TimeText"/>): two equal lists are two identical snapshots, and a row a sync rewrote with
    /// the same values still differs by its last-seen time once the clock has moved.
    /// </summary>
    public IReadOnlyList<string> Rows() =>
    [
        .. Works.Select(work => Line("work", work.Id, work.Kind, work.CreatedUtc)),
        .. Titles.Select(title => Line(
            "title", title.Id, title.MediaWorkId, title.Kind, title.Text, title.NormalizedText, title.Year, Provenance(title))),
        .. Identifiers.Select(identifier => Line(
            "identifier", identifier.Id, identifier.MediaWorkId, identifier.Scheme, identifier.Value, Provenance(identifier))),
        .. Versions.Select(version => Line(
            "version", version.Id, version.MediaWorkId, version.SourceVersionId, version.EditionLabel, Provenance(version))),
        .. Files.Select(file => Line(
            "file",
            file.Id,
            file.MediaVersionId,
            file.ServicePath,
            file.NormalizedServicePath,
            file.SizeBytes,
            file.DeviceId,
            file.Inode,
            file.LibraryLocationId,
            Provenance(file))),
        .. Locations.Select(location => Line("location", location.Id, location.Name, Provenance(location))),
    ];

    /// <summary>
    /// An instant as <see cref="Rows"/> writes it: to the tick, and alike whatever its kind — SQLite gives
    /// a stored time back unspecified, the clock gives it as UTC.
    /// </summary>
    public static string TimeText(DateTime instant) => instant.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static string Provenance(ProvenancedEntity row) =>
        Line(row.SourceKind, row.SourceConnectionId, row.SourceName, row.SourceItemId, row.CreatedUtc, row.ObservedUtc);

    // "∅" for null, so a null and an empty value never print alike.
    private static string Line(params object?[] values) =>
        string.Join(" | ", values.Select(value => value switch
        {
            null => "∅",
            DateTime instant => TimeText(instant),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        }));
}
