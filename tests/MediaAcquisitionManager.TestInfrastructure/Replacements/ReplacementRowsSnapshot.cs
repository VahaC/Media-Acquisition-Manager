using System.Text.Json;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-27 — every replacement row there is, as stored now, with the download jobs and how many audit rows there are: what a
/// request that must write nothing — refused, not found, or a member's — is compared against, before and after. Each row
/// is written out whole, every column by name, so a column a later ticket adds is compared too, without a line here.
/// </summary>
/// <remarks>
/// A real job's rows, not an empty table's count: with nothing planted, "nothing written" only proves nothing was
/// created, while a planted job proves nothing of it was changed or deleted either.
/// </remarks>
/// <param name="Jobs">The replacements, one row per line.</param>
/// <param name="Copies">Their ticked copies.</param>
/// <param name="Works">Their matched works and the works' ids.</param>
/// <param name="Names">The names of their moves.</param>
/// <param name="NewFiles">Their new files and the new files' ids.</param>
/// <param name="Downloads">Every download job.</param>
/// <param name="AuditEvents">How many audit rows there are.</param>
public sealed record ReplacementRowsSnapshot(string Jobs, string Copies, string Works, string Names, string NewFiles, string Downloads, int AuditEvents)
{
    /// <summary>The rows of <paramref name="factory"/>'s database now, each table's by id.</summary>
    public static async Task<ReplacementRowsSnapshot> TakeAsync(MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ReplacementRowsSnapshot? snapshot = null;
        await factory.WithDbAsync(async db => snapshot = new ReplacementRowsSnapshot(
            Text(await db.ReplacementJobs.AsNoTracking().ToListAsync(cancellationToken)),
            Text(await db.ReplacementJobFiles.AsNoTracking().ToListAsync(cancellationToken)),
            Text(await db.ReplacementJobWorks.AsNoTracking().ToListAsync(cancellationToken))
                + "\n" + Text(await db.ReplacementJobWorkIdentifiers.AsNoTracking().ToListAsync(cancellationToken)),
            Text(await db.ReplacementJobNames.AsNoTracking().ToListAsync(cancellationToken)),
            Text(await db.ReplacementJobNewFiles.AsNoTracking().ToListAsync(cancellationToken))
                + "\n" + Text(await db.ReplacementJobNewFileIdentifiers.AsNoTracking().ToListAsync(cancellationToken)),
            Text(await db.DownloadJobs.AsNoTracking().ToListAsync(cancellationToken)),
            await db.AuditEvents.CountAsync(cancellationToken)));
        return snapshot!;
    }

    // Ordered here, by id: a table's own order is whatever SQLite reads it in.
    private static string Text<TEntity>(IEnumerable<TEntity> rows)
        where TEntity : BaseEntity =>
        string.Join("\n", rows.OrderBy(row => row.Id).Select(row => JsonSerializer.Serialize(row)));
}
