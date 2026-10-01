using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <inheritdoc cref="IDestinationService"/>
/// <remarks>
/// Where a destination saves is worked out by <see cref="IDestinationChecker"/>, which asks the clients;
/// this service decides what may be saved and saves it.
/// </remarks>
public sealed class DestinationService(
    ApplicationDbContext db,
    IDestinationChecker checker,
    IAuditTrail auditTrail,
    IDestinationMapper mapper,
    TimeProvider time,
    ILogger<DestinationService> logger) : IDestinationService
{
    private const string DownloadClientMissingError = "Pick the download client this destination belongs to.";

    private const string DownloadClientGoneError = "The download client you picked no longer exists. Reload the page and pick one again.";

    public async Task<DestinationListResult> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return DestinationListResult.Invalid(pagingError);

        var destinations = db.DestinationMappings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matched against the normalised name, so the search ignores letter case — Cyrillic too, as
            // the upper-casing happens in .NET, not in SQLite. Contains becomes instr(), which reads '%'
            // and '_' as themselves (IntegrationConnectionService).
            var normalizedSearch = DestinationRules.NormalizeName(search);
            destinations = destinations.Where(destination => destination.NormalizedName.Contains(normalizedSearch));
        }

        var totalCount = await destinations.CountAsync(cancellationToken);
        // Past the last page there is nothing to read, nor any client to ask; and a page far enough out
        // would overflow the offset, which EF Core takes as an int (the UserAccountService guard).
        var offset = (long)(page - 1) * pageSize;
        List<DestinationRow> rows = offset >= totalCount
            ? []
            : await destinations
                .Join(
                    db.IntegrationConnections,
                    destination => destination.DownloadClientConnectionId,
                    client => client.Id,
                    (destination, client) => new { Destination = destination, ClientName = client.Name, ClientNormalizedName = client.NormalizedName })
                // A name is unique per client only, so the client's name breaks a tie, and the id the last
                // one (two clients may share a name in no letter case, but a row of an older build might):
                // the order is total, and no destination shows on two pages.
                .OrderBy(row => row.Destination.NormalizedName)
                .ThenBy(row => row.ClientNormalizedName)
                .ThenBy(row => row.Destination.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .Select(row => new DestinationRow(row.Destination, row.ClientName))
                .ToListAsync(cancellationToken);

        // MAM-11 — the page's destinations only: a client holding none of them is not asked.
        var checks = await checker.CheckAsync([.. rows.Select(row => row.Destination)], cancellationToken);
        var items = rows.Select(row => mapper.ToDto(row.Destination, row.ClientName, checks[row.Destination.Id])).ToList();
        return DestinationListResult.Listed(new DestinationListResponse(items, totalCount, page, pageSize));
    }

    public async Task<DestinationDto?> GetAsync(Guid destinationId, CancellationToken cancellationToken = default)
    {
        var row = await db.DestinationMappings
            .AsNoTracking()
            .Where(destination => destination.Id == destinationId)
            .Join(
                db.IntegrationConnections,
                destination => destination.DownloadClientConnectionId,
                client => client.Id,
                (destination, client) => new DestinationRow(destination, client.Name))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var checks = await checker.CheckAsync([row.Destination], cancellationToken);
        return mapper.ToDto(row.Destination, row.ClientName, checks[row.Destination.Id]);
    }

    public async Task<DestinationChangeResult> CreateAsync(CreateDestinationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // MAM-11 — checked in this order: the name, the client picked, the category's name, whether the
        // client is a download client the app has, whether the name is free for it, then whether the
        // client confirms the category — one sentence per answer, the first rule broken, and the live call
        // last, so a request the rules refuse never reaches a client.
        if (Validate(request.Name, request.DownloadClientId, request.ClientDestinationKey) is { } error)
            return DestinationChangeResult.Invalid(error);
        var client = await FindDownloadClientAsync(request.DownloadClientId!.Value, cancellationToken);
        if (client is null)
            return DestinationChangeResult.DownloadClientMissing(DownloadClientGoneError);

        var name = request.Name!.Trim();
        var normalizedName = DestinationRules.NormalizeName(name);
        if (await IsNameTakenAsync(client.Id, normalizedName, exceptDestinationId: null, cancellationToken))
            return NameTaken(client, name);

        var key = request.ClientDestinationKey!;
        var (confirmed, refusal) = await ConfirmCategoryAsync(client, key, cancellationToken);
        if (refusal is not null)
            return refusal;

        // Decision 2 — the first destination is the default without being asked, so there is one as soon
        // as there is anything to send downloads to; a later one only when the owner says so.
        var isDefault = request.IsDefault == true || !await db.DestinationMappings.AnyAsync(cancellationToken);
        var actor = await ActorAsync(actorUserId, cancellationToken);
        var destination = new DestinationMappingEntity
        {
            DownloadClientConnectionId = client.Id,
            Name = name,
            NormalizedName = normalizedName,
            // As the client spells it, never trimmed: it is what a download sends (DestinationRules).
            ClientDestinationKey = key,
            IsDefault = isDefault,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.DestinationMappings.Add(destination);
        // One row for the destination the owner added, a default taken off another one included.
        auditTrail.Record(AuditAction.DestinationCreated, actor, AuditTarget.Destination(destination));

        try
        {
            await SaveAsync(destination, takesDefault: isDefault, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (await LostRaceAsync(exception, client, name, normalizedName, exceptDestinationId: null, cancellationToken) is { } lost)
                return lost;
            throw;
        }

        logger.LogInformation(
            "{ActorUserName} added the destination {DestinationName} of integration {IntegrationId}.", actor.UserName, destination.Name, client.Id);
        // The check from the answer that confirmed the category: asking the client again would cost a
        // call and could tell another story than the one the save relied on.
        return DestinationChangeResult.Succeeded(mapper.ToDto(destination, client.Name, confirmed!.Check));
    }

    public async Task<DestinationChangeResult> UpdateAsync(Guid destinationId, UpdateDestinationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var destination = await db.DestinationMappings
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == destinationId, cancellationToken);
        if (destination is null)
            return DestinationChangeResult.NotFound;

        // The create order, against the destination as the save would leave it.
        if (Validate(request.Name, request.DownloadClientId, request.ClientDestinationKey) is { } error)
            return DestinationChangeResult.Invalid(error);
        var client = await FindDownloadClientAsync(request.DownloadClientId!.Value, cancellationToken);
        if (client is null)
            return DestinationChangeResult.DownloadClientMissing(DownloadClientGoneError);

        var name = request.Name!.Trim();
        var normalizedName = DestinationRules.NormalizeName(name);
        if (await IsNameTakenAsync(client.Id, normalizedName, destination.Id, cancellationToken))
            return NameTaken(client, name);

        var key = request.ClientDestinationKey!;
        // Decision 3 — the client is asked only about a new client or a new category: a new name or the
        // default alone doesn't depend on what the client has, so it saves while the client is switched
        // off or away too.
        if (client.Id != destination.DownloadClientConnectionId || !string.Equals(key, destination.ClientDestinationKey, StringComparison.Ordinal))
        {
            var (_, refusal) = await ConfirmCategoryAsync(client, key, cancellationToken);
            if (refusal is not null)
                return refusal;
        }

        var takesDefault = request.IsDefault == true && !destination.IsDefault;
        destination.Name = name;
        destination.NormalizedName = normalizedName;
        destination.DownloadClientConnectionId = client.Id;
        destination.ClientDestinationKey = key;
        // Left out keeps it: a client that doesn't know the field can't take the default away. False
        // leaves the installation without a default until the owner picks one (decision 2).
        if (request.IsDefault is { } isDefault)
            destination.IsDefault = isDefault;

        // MAM-4's rule: a save that changes nothing records nothing. Asked before the audit row is added,
        // which is a change itself.
        if (!db.ChangeTracker.HasChanges())
            return DestinationChangeResult.Succeeded();

        var actor = await ActorAsync(actorUserId, cancellationToken);
        auditTrail.Record(AuditAction.DestinationUpdated, actor, AuditTarget.Destination(destination));

        try
        {
            await SaveAsync(destination, takesDefault, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by another request since it was read: nothing of this one was saved.
            db.ChangeTracker.Clear();
            return DestinationChangeResult.NotFound;
        }
        catch (DbUpdateException exception)
        {
            if (await LostRaceAsync(exception, client, name, normalizedName, destinationId, cancellationToken) is { } lost)
                return lost;
            throw;
        }

        logger.LogInformation(
            "{ActorUserName} changed the destination {DestinationName} of integration {IntegrationId}.", actor.UserName, destination.Name, client.Id);
        return DestinationChangeResult.Succeeded();
    }

    public async Task<DestinationChangeResult> DeleteAsync(Guid destinationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var destination = await db.DestinationMappings
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == destinationId, cancellationToken);
        if (destination is null)
            return DestinationChangeResult.NotFound;

        var actor = await ActorAsync(actorUserId, cancellationToken);
        // The audit row copies the name: it has to say what was removed after the row is gone.
        auditTrail.Record(AuditAction.DestinationDeleted, actor, AuditTarget.Destination(destination));
        // A tracked delete rather than ExecuteDelete, so it commits in the same save as its audit row. The
        // database does the rest: the download jobs sent to it stay, their link set to null and its name
        // kept. Decision 2 — a removed default is not replaced: picking the next one is the owner's call.
        db.DestinationMappings.Remove(destination);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by another request since it was read (a double click): gone either way, and that
            // request recorded it.
            db.ChangeTracker.Clear();
            return DestinationChangeResult.NotFound;
        }

        logger.LogInformation(
            "{ActorUserName} removed the destination {DestinationName} of integration {IntegrationId}.",
            actor.UserName,
            destination.Name,
            destination.DownloadClientConnectionId);
        return DestinationChangeResult.Succeeded();
    }

    public async Task<ClientDestinationListResponse?> ListClientDestinationsAsync(Guid downloadClientId, CancellationToken cancellationToken = default)
    {
        var client = await FindDownloadClientAsync(downloadClientId, cancellationToken);
        return client is null ? null : mapper.ToResponse(await checker.ListAsync(client, cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The rules a create and an update share, in their order: the name, the client picked, then the
    // category's name.
    private static string? Validate(string? name, Guid? downloadClientId, string? clientDestinationKey)
    {
        if (DestinationRules.ValidateName(name) is { } nameError)
            return nameError;
        if (downloadClientId is null)
            return DownloadClientMissingError;
        return DestinationRules.ValidateClientDestinationKey(clientDestinationKey);
    }

    // Not a download client is the same as none: a destination names a client's category, and only a
    // download client has those. Read with its path mappings, which the check maps the client's folders
    // through.
    private Task<IntegrationConnectionEntity?> FindDownloadClientAsync(Guid downloadClientId, CancellationToken cancellationToken) =>
        db.IntegrationConnections
            .AsNoTracking()
            .Include(client => client.PathMappings)
            .SingleOrDefaultAsync(client => client.Id == downloadClientId && client.Kind == IntegrationKind.DownloadClient, cancellationToken);

    private Task<bool> IsNameTakenAsync(Guid downloadClientId, string normalizedName, Guid? exceptDestinationId, CancellationToken cancellationToken) =>
        db.DestinationMappings.AnyAsync(
            other => other.DownloadClientConnectionId == downloadClientId
                && other.NormalizedName == normalizedName
                && other.Id != exceptDestinationId,
            cancellationToken);

    /// <summary>
    /// Decision 3 — asks <paramref name="client"/> whether it has the category <paramref name="key"/>,
    /// exactly as spelled: a destination is saved only once its client confirms it, so a typo never
    /// becomes a category the client would create on the first download. The confirmed category with its
    /// check, or the refusal: 409 when the client can't be asked or has no such category, 502 with the
    /// client's sentence when it was asked and couldn't answer.
    /// </summary>
    private async Task<(CheckedClientDestination? Confirmed, DestinationChangeResult? Refusal)> ConfirmCategoryAsync(
        IntegrationConnectionEntity client, string key, CancellationToken cancellationToken)
    {
        var listed = await checker.ListAsync(client, cancellationToken);
        switch (listed.Status)
        {
            case ClientDestinationsStatus.Listed:
                break;
            case ClientDestinationsStatus.CallFailed:
                return (null, DestinationChangeResult.ClientFailed(listed.Error!));
            default:
                return (null, DestinationChangeResult.ClientUnusable(listed.Error!));
        }

        var confirmed = listed.Destinations.FirstOrDefault(candidate => string.Equals(candidate.Destination.Key, key, StringComparison.Ordinal));
        return confirmed is null
            ? (null, DestinationChangeResult.CategoryMissing(
                $"“{client.Name}” has no category “{key}”. Create it in the client first, or pick one it has."))
            : (confirmed, null);
    }

    /// <summary>
    /// Saves the change with its audit row. Decision 2 — a destination taking the default clears the old
    /// one first, by a statement of its own in the same transaction: SQLite checks the partial unique
    /// index as each row changes, not at commit, and two saves that each take the default run one after
    /// the other (SQLite has one writer), the second clearing the first's. Disposed without a commit, the
    /// transaction rolls both back. A save that leaves the default alone is the one statement batch
    /// <c>SaveChanges</c> runs in its own transaction.
    /// </summary>
    private async Task SaveAsync(DestinationMappingEntity destination, bool takesDefault, CancellationToken cancellationToken)
    {
        if (!takesDefault)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.DestinationMappings
            .Where(other => other.IsDefault && other.Id != destination.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(other => other.IsDefault, false), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The answer to a save the database refused because another request got there first, or <c>null</c>
    /// when it was something else (a full disk), which stays a failure. The MAM-4 race: two saves of one
    /// name for one client can both pass the check, and the unique index lets only one commit; the client
    /// removed between the check and the save breaks the foreign key instead.
    /// </summary>
    private async Task<DestinationChangeResult?> LostRaceAsync(
        DbUpdateException exception,
        IntegrationConnectionEntity client,
        string name,
        string normalizedName,
        Guid? exceptDestinationId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        if (await IsNameTakenAsync(client.Id, normalizedName, exceptDestinationId, cancellationToken))
        {
            logger.LogInformation(
                exception, "A concurrent request gave a destination of integration {IntegrationId} this name first; this change was not saved.", client.Id);
            return NameTaken(client, name);
        }

        if (await FindDownloadClientAsync(client.Id, cancellationToken) is null)
        {
            logger.LogInformation(
                exception, "Integration {IntegrationId} was removed before this destination change was saved; nothing was saved.", client.Id);
            return DestinationChangeResult.DownloadClientMissing(DownloadClientGoneError);
        }

        return null;
    }

    // Read from the database rather than the token: the audit row carries the name the account has.
    private Task<AuditActor> ActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    private static DestinationChangeResult NameTaken(IntegrationConnectionEntity client, string name) =>
        DestinationChangeResult.NameTaken($"“{client.Name}” already has a destination named “{name}”.");

    /// <summary>A destination and the name of its download client, read together.</summary>
    private sealed record DestinationRow(DestinationMappingEntity Destination, string ClientName);
}
