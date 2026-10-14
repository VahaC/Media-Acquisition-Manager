using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementOptionsReader"/>
public sealed class ReplacementOptionsReader(
    ApplicationDbContext db,
    IReplacementGates gates,
    IReleaseMatchReader matches,
    IDestinationChecker destinations,
    IDownloadOverwriteGuard overwrites,
    IReplacementCandidateReader candidates,
    IReplacementHoldingReader torrents,
    IReplacementMapper mapper,
    TimeProvider time,
    ILogger<ReplacementOptionsReader> logger) : IReplacementOptionsReader
{
    /// <summary>R1 — the destination's folder isn't confirmed inside a library, and its check has no sentence of its
    /// own.</summary>
    private const string NotInLibraryProblem = "The destination's folder isn't confirmed inside a library the app knows.";

    /// <summary>R1 — a replacement is sent only after the check for files already where it lands (ADR 0034), which a
    /// folder outside the Media folders, or without a path in the app, can't have.</summary>
    private const string FolderUncheckedProblem =
        "The destination's folder lies outside the Media folders or has no path in the app, so the app can't check it for files already there, and a replacement is sent only after that check.";

    /// <summary>A destination whose client was removed after the destination was read.</summary>
    private const string ClientGoneProblem = "The download client of this destination no longer exists.";

    public async Task<ReplacementOptionsResult> ReadAsync(
        string? releaseId, string? destinationId, string? holdings, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        Guid? release = Guid.TryParse(releaseId, out var parsedRelease) ? parsedRelease : null;
        Guid? destination = Guid.TryParse(destinationId, out var parsedDestination) ? parsedDestination : null;
        if (DownloadRules.ValidateReleaseId(release) is { } releaseError)
            return ReplacementOptionsResult.Invalid(releaseError);
        if (DownloadRules.ValidateDestinationId(destination) is { } destinationError)
            return ReplacementOptionsResult.Invalid(destinationError);
        if (ReplacementRules.ValidateHoldings(holdings, out var readHoldings) is { } holdingsError)
            return ReplacementOptionsResult.Invalid(holdingsError);

        // The release first, as a send checks it: another user's answers as missing, whatever else the query says.
        if (await CallerSearches.FindReleaseAsync(db, release!.Value, callerUserId, time, cancellationToken) is not { } found)
            return ReplacementOptionsResult.ReleaseMissing(DownloadErrors.ReleaseMissing);
        if (await db.DestinationMappings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == destination, cancellationToken) is not { } picked)
            return ReplacementOptionsResult.DestinationMissing(DownloadErrors.DestinationGone);

        var editions = ReplacementReleaseEditions.Of(found.Release);
        // Read fresh, never cached (ADR 0033): a Save of the Media folders page counts at the next read.
        var gate = await gates.ReadAsync(cancellationToken);
        // ADR 0033 — off, nothing is read: no client asked, no work, no disk.
        if (!gate.IsEnabled)
            return Unavailable(ReplacementUnavailableReason.SwitchedOff, null, editions);
        // ADR 0032 — a magnet's files are unknown until the client has the job, so nothing can be checked before the send.
        if (found.Release.PayloadKind == ReleasePayloadKind.Magnet)
            return Unavailable(ReplacementUnavailableReason.Magnet, null, editions);
        if (await DestinationProblemAsync(picked, cancellationToken) is { } problem)
            return Unavailable(ReplacementUnavailableReason.DestinationNotUsable, problem, editions);

        // The owner's view of the works: the route is the owner's alone.
        var matched = await matches.MatchAsync(found, forOwner: true, cancellationToken);
        if (matched.Release.LibraryMatches.Count == 0)
            return Unavailable(ReplacementUnavailableReason.NoMatchedWork, null, editions);

        var works = await candidates.ReadAsync(matched, gate, cancellationToken);
        List<ReplacementCandidateCopy> tickable = [.. works.SelectMany(work => work.TickableCopies)];
        // R2 — the torrents only on the dialog's word, and only while a copy can be ticked: nothing of the owner's
        // clients is read for a release that can replace nothing.
        var held = readHoldings && tickable.Count > 0
            ? await torrents.ReadAsync(tickable, gate.AllowedRoots, cancellationToken)
            : ReplacementHoldings.None;

        logger.LogInformation(
            "Replacement options of release {ReleaseId} through destination {DestinationId}: {WorkCount} works, {TickableCount} copies can be ticked, {ClientCount} clients read.",
            found.Release.Id,
            picked.Id,
            works.Count,
            tickable.Count,
            held.Clients.Count);
        return ReplacementOptionsResult.Listed(mapper.ToResponse(null, null, editions, works, held));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private ReplacementOptionsResult Unavailable(ReplacementUnavailableReason reason, string? problem, IReadOnlyList<MediaEdition> editions)
    {
        logger.LogInformation("Replacement options answered {Reason}.", reason);
        return ReplacementOptionsResult.Listed(mapper.ToResponse(reason, problem, editions, [], ReplacementHoldings.None));
    }

    /// <summary>
    /// R1 — why the destination can't take a replacement, or <c>null</c> when it can: its client couldn't be asked where
    /// it saves or no longer has its category, its folder isn't confirmed inside a library the app knows, or the app
    /// can't look into it for files already there. Asked of its client the way the send's own check asks it
    /// (<see cref="IDestinationChecker.ListAsync"/>, <see cref="IDownloadOverwriteGuard.FolderReasonAsync"/>).
    /// </summary>
    private async Task<string?> DestinationProblemAsync(DestinationMappingEntity destination, CancellationToken cancellationToken)
    {
        // Not a download client is the same as none (the DestinationService rule); read with its path mappings, which the
        // category's folder is mapped through.
        if (await db.IntegrationConnections
                .AsNoTracking()
                .Include(client => client.PathMappings)
                .SingleOrDefaultAsync(
                    client => client.Id == destination.DownloadClientConnectionId && client.Kind == IntegrationKind.DownloadClient,
                    cancellationToken) is not { } client)
            return ClientGoneProblem;

        // MAM-30 — a folder client is proposed this destination's folder, found by folder (D2, D3): one it can't see is
        // FolderNotSeen with the client's sentence, which the check's problem below carries.
        var listing = await destinations.ListAsync(client, [destination.ClientDestinationKey], cancellationToken);
        if (listing.Status != ClientDestinationsStatus.Listed)
            return $"The app couldn't ask “{client.Name}” where this destination saves: {listing.Error}";
        // Only a category can be missing: the listing always holds a folder client's stored folder, the client's own entry
        // or one ListAsync adds with why the app can't use it (D3).
        if (listing.Find(destination.ClientDestinationKey) is not { } listed)
            return $"The app couldn't ask “{client.Name}” where this destination saves: it has no category “{destination.ClientDestinationKey}” any more.";
        if (listed.Check.Status != DestinationPathStatus.InLibrary)
            return listed.Check.Problem ?? NotInLibraryProblem;
        return await overwrites.FolderReasonAsync(listed, cancellationToken) is null ? null : FolderUncheckedProblem;
    }
}
