using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <inheritdoc cref="ICopyRemovalVerdicts"/>
public sealed class CopyRemovalVerdicts(ApplicationDbContext db, IReplacementGates gates) : ICopyRemovalVerdicts
{
    public async Task<CopyRemovalJudge> ReadAsync(CancellationToken cancellationToken = default) =>
        new(await gates.ReadAsync(cancellationToken), await CopyRemovalHolds.ReadAsync(db, cancellationToken));
}
