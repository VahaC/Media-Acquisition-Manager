using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// MAM-27 — what a check of the new files leaves behind, forgotten in one place for both who start it over: the owner's
// Retry of a check that stopped, and the verifier's files stage, which replaces the new files whole (ADR 0035).
public static partial class ReplacementJobUpdates
{
    /// <summary>
    /// Forgets the last check of the replacement <paramref name="replacementJobId"/>: deletes its new-file rows — their
    /// ids go with them, by the database's cascade — and clears every undropped copy's pair, problem and drop-only mark,
    /// so the page never shows the last run's evidence as the current one's and a new pairing starts from nothing. A
    /// dropped copy keeps its last problem and pair as its history: no check reads it again. Staged in the caller's
    /// transaction, and never cancelled, as every write here.
    /// </summary>
    public static async Task ForgetCheckAsync(this ApplicationDbContext db, Guid replacementJobId)
    {
        await db.ReplacementJobNewFiles.Where(file => file.ReplacementJobId == replacementJobId).ExecuteDeleteAsync(CancellationToken.None);
        await db.ReplacementJobFiles
            .Where(copy => copy.ReplacementJobId == replacementJobId
                && copy.DroppedUtc == null
                && (copy.PairedNewFileId != null || copy.Problem != null || copy.OnlyDropSettles))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(copy => copy.PairedNewFileId, (Guid?)null)
                    .SetProperty(copy => copy.Problem, (Message?)null)
                    .SetProperty(copy => copy.OnlyDropSettles, false),
                CancellationToken.None);
    }
}
