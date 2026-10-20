using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Texts;
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
    IReleaseSourceCategoryReader sectionReader,
    IAuditTrail auditTrail,
    IDestinationMapper mapper,
    IIntegrationAdapterRegistry adapters,
    TimeProvider time,
    ILogger<DestinationService> logger) : IDestinationService
{
    private static readonly Message DownloadClientMissingError = Message.Of("destinations.client.not-picked");

    /// <summary>MAM-47 — the send's own sentence, word for word: one key (<see cref="DownloadErrors.ClientGone"/>).</summary>
    private static readonly Message DownloadClientGoneError = DownloadErrors.ClientGone;

    private static readonly Message SourceGoneError = Message.Of("destinations.section.source-gone");

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
                    (destination, client) => new
                    {
                        Destination = destination,
                        ClientName = client.Name,
                        ClientNormalizedName = client.NormalizedName,
                        ClientAdapterType = client.AdapterType,
                    })
                // A name is unique per client only, so the client's name breaks a tie, and the id the last
                // one (two clients may share a name in no letter case, but a row of an older build might):
                // the order is total, and no destination shows on two pages.
                .OrderBy(row => row.Destination.NormalizedName)
                .ThenBy(row => row.ClientNormalizedName)
                .ThenBy(row => row.Destination.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .Select(row => new DestinationRow(row.Destination, row.ClientName, row.ClientAdapterType))
                .ToListAsync(cancellationToken);

        // MAM-11 — the page's destinations only: a client holding none of them is not asked.
        var checks = await checker.CheckAsync([.. rows.Select(row => row.Destination)], cancellationToken);
        var items = rows.Select(row => ToDto(row, checks[row.Destination.Id])).ToList();
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
                (destination, client) => new DestinationRow(destination, client.Name, client.AdapterType))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var checks = await checker.CheckAsync([row.Destination], cancellationToken);
        return ToDto(row, checks[row.Destination.Id]);
    }

    public async Task<DestinationChangeResult> CreateAsync(CreateDestinationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // MAM-11 — checked in this order: the name, the client picked, whether the client is a download client the app
        // has, (MAM-31) the category's or folder's name in the words of that client, (MAM-32) the sections, (MAM-30)
        // whether the name can be the client's label, (MAM-32) whether the sections' sources are release sources it has,
        // whether the name is free for the client, then whether the client confirms the category — one sentence per
        // answer, the first rule broken, and the live call last, so a request the rules refuse never reaches a client.
        if (Validate(request.Name, request.DownloadClientId) is { } error)
            return DestinationChangeResult.Invalid(error);
        var client = await FindDownloadClientAsync(request.DownloadClientId!.Value, cancellationToken);
        if (client is null)
            return DestinationChangeResult.DownloadClientMissing(DownloadClientGoneError);
        if (Validate(client, request.ClientDestinationKey, request.Sections) is { } fieldError)
            return DestinationChangeResult.Invalid(fieldError);
        if (LabelError(client, request.Name!) is { } labelError)
            return DestinationChangeResult.Invalid(labelError);
        // A new destination has no sections of its own to take off: none picked is nothing to map.
        var sections = Pick(request.Sections) is { Count: > 0 } picked ? picked : null;
        if (!await AreSourcesThereAsync(sections, cancellationToken))
            return DestinationChangeResult.SourceMissing(SourceGoneError);

        var name = request.Name!.Trim();
        var normalizedName = DestinationRules.NormalizeName(name);
        if (await IsNameTakenAsync(client.Id, normalizedName, exceptDestinationId: null, cancellationToken))
            return NameTaken(client, name);

        var (confirmed, refusal) = await ConfirmAsync(client, request.ClientDestinationKey!, cancellationToken);
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
            // As the client spells it, never trimmed: it is what a download sends (DestinationRules). MAM-30 — the
            // confirmed entry's own key, never the request's: a folder in the client's canonical spelling (D4).
            ClientDestinationKey = confirmed!.Destination.Key,
            IsDefault = isDefault,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.DestinationMappings.Add(destination);

        try
        {
            // One audit row for the destination the owner added, a default taken off another one and the
            // sections moved here from others included.
            await SaveAsync(destination, takesDefault: isDefault, sections, AuditAction.DestinationCreated, actor, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (await LostRaceAsync(exception, client, name, normalizedName, exceptDestinationId: null, sections, cancellationToken) is { } lost)
                return lost;
            throw;
        }

        logger.LogInformation(
            "{ActorUserName} added the destination {DestinationName} of integration {IntegrationId}.", actor.UserName, destination.Name, client.Id);
        // The check from the answer that confirmed the category: asking the client again would cost a
        // call and could tell another story than the one the save relied on.
        return DestinationChangeResult.Succeeded(mapper.ToDto(destination, client.Name, confirmed.Check, DestinationKindOf(client.AdapterType)));
    }

    public async Task<DestinationChangeResult> UpdateAsync(Guid destinationId, UpdateDestinationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var destination = await db.DestinationMappings
            .AsTracking()
            .SingleOrDefaultAsync(row => row.Id == destinationId, cancellationToken);
        if (destination is null)
            return DestinationChangeResult.NotFound;

        // The create order, against the destination as the save would leave it.
        if (Validate(request.Name, request.DownloadClientId) is { } error)
            return DestinationChangeResult.Invalid(error);
        var client = await FindDownloadClientAsync(request.DownloadClientId!.Value, cancellationToken);
        if (client is null)
            return DestinationChangeResult.DownloadClientMissing(DownloadClientGoneError);
        if (Validate(client, request.ClientDestinationKey, request.Sections) is { } fieldError)
            return DestinationChangeResult.Invalid(fieldError);
        // MAM-30 — a rename alone too: it asks no client, but the client shows the new name all the same.
        if (LabelError(client, request.Name!) is { } labelError)
            return DestinationChangeResult.Invalid(labelError);
        // MAM-32 — left out keeps the destination's sections; empty takes every one off it.
        var sections = Pick(request.Sections);
        if (!await AreSourcesThereAsync(sections, cancellationToken))
            return DestinationChangeResult.SourceMissing(SourceGoneError);

        var name = request.Name!.Trim();
        var normalizedName = DestinationRules.NormalizeName(name);
        if (await IsNameTakenAsync(client.Id, normalizedName, destination.Id, cancellationToken))
            return NameTaken(client, name);

        var key = request.ClientDestinationKey!;
        // Decision 3 — the client is asked only about a new client or a new category: a new name or the
        // default alone doesn't depend on what the client has, so it saves while the client is switched
        // off or away too. MAM-30 — a folder is the same folder in any spelling (D2), and keeps the stored one; a new
        // one is stored as the client confirmed it (D4).
        if (client.Id != destination.DownloadClientConnectionId || !IsSameKey(client, key, destination.ClientDestinationKey))
        {
            var (confirmed, refusal) = await ConfirmAsync(client, key, cancellationToken);
            if (refusal is not null)
                return refusal;
            key = confirmed!.Destination.Key;
        }
        else
        {
            key = destination.ClientDestinationKey;
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

        var actor = await ActorAsync(actorUserId, cancellationToken);
        try
        {
            // MAM-4's rule: a save that changes nothing records nothing.
            if (!await SaveAsync(destination, takesDefault, sections, AuditAction.DestinationUpdated, actor, cancellationToken))
                return DestinationChangeResult.Succeeded();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by another request since it was read: nothing of this one was saved.
            db.ChangeTracker.Clear();
            return DestinationChangeResult.NotFound;
        }
        catch (DbUpdateException exception)
        {
            if (await LostRaceAsync(exception, client, name, normalizedName, destinationId, sections, cancellationToken) is { } lost)
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

    public async Task<ClientDestinationListResponse?> ListClientDestinationsAsync(
        Guid downloadClientId, Guid? editedDestinationId, CancellationToken cancellationToken = default)
    {
        var client = await FindDownloadClientAsync(downloadClientId, cancellationToken);
        if (client is null)
            return null;

        // MAM-30 — the Edit dialog's own destination's folder, so a folder client lists it as itself even once no library
        // folder leads to it any more (D4). Stored, not typed, so the owner still picks only what the client confirms. One
        // of another client — the dialog switched clients — or none at all proposes nothing.
        var storedKeys = editedDestinationId is { } destinationId
            ? await db.DestinationMappings
                .AsNoTracking()
                .Where(destination => destination.Id == destinationId && destination.DownloadClientConnectionId == client.Id)
                .Select(destination => destination.ClientDestinationKey)
                .ToListAsync(cancellationToken)
            : [];
        return mapper.ToResponse(await checker.ListAsync(client, storedKeys, cancellationToken), DestinationKindOf(client.AdapterType));
    }

    public async Task<DestinationSectionListResponse> ListSectionsAsync(CancellationToken cancellationToken = default)
    {
        // Every database read happens before the sources are asked: the context is not safe to share
        // between the concurrent calls (ReleaseSources' rule). The normalised name is unique, so the order
        // is total.
        var sources = await db.IntegrationConnections
            .AsNoTracking()
            .Where(integration => integration.Kind == IntegrationKind.ReleaseSearch)
            .OrderBy(integration => integration.NormalizedName)
            .ToListAsync(cancellationToken);
        var mapped = (await db.DestinationSections.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(section => section.ReleaseSourceConnectionId);
        var destinationNames = await db.DestinationMappings
            .AsNoTracking()
            .Select(destination => new { destination.Id, destination.Name })
            .ToDictionaryAsync(destination => destination.Id, destination => destination.Name, cancellationToken);

        var answers = await sectionReader.ReadAsync(sources, cancellationToken);
        return new DestinationSectionListResponse(
        [
            .. sources.Select((source, index) => mapper.ToDto(
                source,
                DestinationSectionGrouping.Group(answers[index].Categories, [.. mapped[source.Id]]),
                destinationNames,
                answers[index].Error)),
        ]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The rules a create and an update share before the client is read, in their order: the name, then the client
    // picked. MAM-31 — the rest wait for the client (the overload below).
    private static Message? Validate(string? name, Guid? downloadClientId)
    {
        if (DestinationRules.ValidateName(name) is { } nameError)
            return nameError;
        return downloadClientId is null ? DownloadClientMissingError : null;
    }

    // MAM-31 — the rules a create and an update share once the client is found, in their order: the category's or
    // folder's name, a missing one asked for in the words of what the client's destinations are (the live check showed
    // "Pick the category or folder" with Transmission alone), then (MAM-32) each section picked — the destination's
    // own fields still before its sections.
    private Message? Validate(IntegrationConnectionEntity client, string? clientDestinationKey, IReadOnlyList<DestinationSectionRequest?>? sections)
    {
        if (DestinationRules.ValidateClientDestinationKey(clientDestinationKey, DestinationKindOf(client.AdapterType), client.Name) is { } keyError)
            return keyError;
        foreach (var section in sections ?? [])
        {
            if (DestinationSectionRules.Validate(section?.ReleaseSourceId, section?.Name, section?.CategoryIds) is { } sectionError)
                return sectionError;
        }

        return null;
    }

    /// <summary>
    /// MAM-32 — the sections of a request the rules let through, one per category of a source: the first of
    /// a category picked twice, which means the same either way — its releases go here. <c>null</c> when the
    /// request left them out.
    /// </summary>
    private static List<PickedSection>? Pick(IReadOnlyList<DestinationSectionRequest?>? sections)
    {
        if (sections is null)
            return null;

        var picked = new List<PickedSection>();
        var seen = new HashSet<(Guid SourceId, string CategoryId)>();
        foreach (var section in sections)
        {
            foreach (var categoryId in section!.CategoryIds!)
            {
                if (seen.Add((section.ReleaseSourceId!.Value, categoryId!)))
                    picked.Add(new PickedSection(section.ReleaseSourceId.Value, categoryId!, DestinationSectionRules.StoredName(section.Name!), section.IsSourceSpecific));
            }
        }

        return picked;
    }

    // Not a release search integration is the same as none: only a release source has sections. A switched
    // off one is still there: the owner may map its sections before switching it on.
    private async Task<bool> AreSourcesThereAsync(IReadOnlyList<PickedSection>? sections, CancellationToken cancellationToken)
    {
        if (sections is not { Count: > 0 })
            return true;

        var sourceIds = sections.Select(section => section.SourceId).Distinct().ToList();
        var found = await db.IntegrationConnections.CountAsync(
            integration => sourceIds.Contains(integration.Id) && integration.Kind == IntegrationKind.ReleaseSearch, cancellationToken);
        return found == sourceIds.Count;
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
    /// <remarks>
    /// MAM-30 — a folder client the same way, by folder (D4): the key must be one of the folders it lists — the library
    /// folders mapped back and its own download folder, the request's key never proposed, so the owner can't type a
    /// folder — and one it can use; a folder it lists but can't see is refused with its own sentence.
    /// </remarks>
    private async Task<(CheckedClientDestination? Confirmed, DestinationChangeResult? Refusal)> ConfirmAsync(
        IntegrationConnectionEntity client, string key, CancellationToken cancellationToken)
    {
        var listed = await checker.ListAsync(client, [], cancellationToken);
        switch (listed.Status)
        {
            case ClientDestinationsStatus.Listed:
                break;
            case ClientDestinationsStatus.CallFailed:
                return (null, DestinationChangeResult.ClientFailed(listed.Error!));
            default:
                return (null, DestinationChangeResult.ClientUnusable(listed.Error!));
        }

        var confirmed = listed.Find(key);
        if (listed.Kind != DownloadDestinationKind.Folder)
        {
            return confirmed is null
                ? (null, DestinationChangeResult.CategoryMissing(
                    Message.Of("destinations.client.category-missing", ("client", client.Name), ("category", key))))
                : (confirmed, null);
        }

        if (confirmed is null)
            return (null, DestinationChangeResult.CategoryMissing(Message.Of("destinations.client.folder-not-offered", ("client", client.Name), ("folder", key))));
        return confirmed.Check.Status == DestinationPathStatus.FolderNotSeen
            ? (null, DestinationChangeResult.CategoryMissing(confirmed.Check.Problem!))
            : (confirmed, null);
    }

    /// <summary>
    /// MAM-30 — why <paramref name="name"/> can't be the name of a destination of <paramref name="client"/>: a folder client
    /// shows it as the torrent's label, and a label can't hold a comma (Transmission refuses the add otherwise, ADR
    /// 0039). <c>null</c> when it can, and for every category client. Asks no client: the type's capabilities say it.
    /// </summary>
    private Message? LabelError(IntegrationConnectionEntity client, string name) =>
        DestinationKindOf(client.AdapterType) == DownloadDestinationKind.Folder && DestinationRules.ValidateLabel(name.Trim()) is not null
            ? Message.Of("destinations.label.comma-in-name", ("client", client.Name))
            : null;

    /// <summary>
    /// MAM-30 — whether a request's key is the destination's stored one, which then asks nobody (decision 3): a category
    /// by its exact name, a folder by being the same folder in any spelling (D2).
    /// </summary>
    private bool IsSameKey(IntegrationConnectionEntity client, string key, string storedKey) =>
        DestinationKindOf(client.AdapterType) == DownloadDestinationKind.Folder
            ? ClientDestinationsResult.IsSameFolder(key, storedKey)
            : string.Equals(key, storedKey, StringComparison.Ordinal);

    /// <summary>
    /// Saves the change with its audit row, and says whether there was one: MAM-4's rule — a save that
    /// changes nothing records nothing, asked before the audit row is added, which is a change itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Decision 2 — a destination taking the default clears the old one first, by a statement of its own in
    /// the same transaction: SQLite checks the partial unique index as each row changes, not at commit, and
    /// two saves that each take the default run one after the other (SQLite has one writer), the second
    /// clearing the first's. Disposed without a commit, the transaction rolls both back.
    /// </para>
    /// <para>
    /// MAM-32 — <paramref name="sections"/>, when given, are mapped inside the same transaction, read and
    /// changed under its write lock (<see cref="MapSectionsAsync"/>): read before it, a section another save
    /// moved in between would be written back over what that save did.
    /// </para>
    /// <para>
    /// A save that leaves the default and the sections alone is the one statement batch
    /// <c>SaveChanges</c> runs in its own transaction.
    /// </para>
    /// </remarks>
    private async Task<bool> SaveAsync(
        DestinationMappingEntity destination,
        bool takesDefault,
        IReadOnlyList<PickedSection>? sections,
        AuditAction action,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        await using var transaction = takesDefault || sections is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (sections is not null)
            await MapSectionsAsync(destination, sections, cancellationToken);
        if (!db.ChangeTracker.HasChanges())
            return false;

        auditTrail.Record(action, actor, AuditTarget.Destination(destination));
        if (takesDefault)
        {
            await db.DestinationMappings
                .Where(other => other.IsDefault && other.Id != destination.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(other => other.IsDefault, false), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// MAM-32 — makes <paramref name="sections"/> the destination's own, tracked for the save: a section
    /// mapped to another destination moves here — the row itself, as one section goes to one destination
    /// (ADR 0020) —, one mapped here keeps its row with the name it is sent with, one mapped nowhere is
    /// added, and every section of the destination not among them is taken off. Unchanged, it changes
    /// nothing, so a save of the sections as they are records nothing.
    /// </summary>
    private async Task MapSectionsAsync(DestinationMappingEntity destination, IReadOnlyList<PickedSection> sections, CancellationToken cancellationToken)
    {
        var sourceIds = sections.Select(section => section.SourceId).Distinct().ToList();
        var categoryIds = sections.Select(section => section.CategoryId).Distinct().ToList();
        // The destination's own and every row of a picked category, wherever mapped; one row per category
        // of a source (the unique index), so a key finds at most one.
        var rows = (await db.DestinationSections
                .AsTracking()
                .Where(row => row.DestinationMappingId == destination.Id
                    || (sourceIds.Contains(row.ReleaseSourceConnectionId) && categoryIds.Contains(row.CategoryId)))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => (row.ReleaseSourceConnectionId, row.CategoryId));

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var section in sections)
        {
            if (rows.Remove((section.SourceId, section.CategoryId), out var row))
            {
                row.DestinationMappingId = destination.Id;
                row.Name = section.Name;
                row.IsSourceSpecific = section.IsSourceSpecific;
                continue;
            }

            db.DestinationSections.Add(new DestinationSectionEntity
            {
                DestinationMappingId = destination.Id,
                ReleaseSourceConnectionId = section.SourceId,
                CategoryId = section.CategoryId,
                Name = section.Name,
                IsSourceSpecific = section.IsSourceSpecific,
                CreatedUtc = now,
            });
        }

        // What is left was read for a picked category of another source, or is the destination's own and
        // not picked: only the latter goes.
        db.DestinationSections.RemoveRange(rows.Values.Where(row => row.DestinationMappingId == destination.Id));
    }

    /// <summary>
    /// The answer to a save the database refused because another request got there first, or <c>null</c>
    /// when it was something else (a full disk), which stays a failure. The MAM-4 race: two saves of one
    /// name for one client can both pass the check, and the unique index lets only one commit; the client
    /// removed between the check and the save breaks the foreign key instead — and so (MAM-32) does a
    /// section's source removed then, or the edited destination itself, which a section added to it names.
    /// </summary>
    private async Task<DestinationChangeResult?> LostRaceAsync(
        DbUpdateException exception,
        IntegrationConnectionEntity client,
        string name,
        string normalizedName,
        Guid? exceptDestinationId,
        IReadOnlyList<PickedSection>? sections,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        // An added section's row can reach the database before the edit of its destination does, and fails
        // on the key rather than finding no row to update: the same bare 404 either way.
        if (exceptDestinationId is { } destinationId && !await db.DestinationMappings.AnyAsync(row => row.Id == destinationId, cancellationToken))
        {
            logger.LogInformation(exception, "Destination {DestinationId} was removed before this change of it was saved; nothing was saved.", destinationId);
            return DestinationChangeResult.NotFound;
        }

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

        if (!await AreSourcesThereAsync(sections, cancellationToken))
        {
            logger.LogInformation(exception, "A release source was removed before this destination change was saved; nothing was saved.");
            return DestinationChangeResult.SourceMissing(SourceGoneError);
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
        DestinationChangeResult.NameTaken(Message.Of("destinations.name.taken", ("client", client.Name), ("name", name)));

    private DestinationDto ToDto(DestinationRow row, DestinationPathCheckResult check) =>
        mapper.ToDto(row.Destination, row.ClientName, check, DestinationKindOf(row.ClientAdapterType));

    // MAM-30 — what a client type's destinations are, asked of the registry by capability (ADR 0005); null for a type
    // this build doesn't register.
    private DownloadDestinationKind? DestinationKindOf(string adapterType) =>
        adapters.Find<IDownloadClient>(adapterType)?.Capabilities.DestinationKind;

    /// <summary>A destination and the name of its download client, read together — MAM-30, with the client's type.</summary>
    private sealed record DestinationRow(DestinationMappingEntity Destination, string ClientName, string ClientAdapterType);

    /// <summary>MAM-32 — one category of a release source a save maps to the destination, with the name and
    /// rank of the section it was picked in.</summary>
    private sealed record PickedSection(Guid SourceId, string CategoryId, string Name, bool IsSourceSpecific);
}
