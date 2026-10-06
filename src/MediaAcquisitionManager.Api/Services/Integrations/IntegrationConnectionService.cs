using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <inheritdoc cref="IIntegrationConnectionService"/>
/// <remarks>
/// Split by facet: this file lists, reads and changes integrations;
/// <c>IntegrationConnectionService.ConnectionTests.cs</c> tests them. The adapter of a type is found
/// through <see cref="IIntegrationAdapterRegistry"/> (Strategy, ADR 0005), never by naming a product.
/// </remarks>
public sealed partial class IntegrationConnectionService(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IEncryptionService encryption,
    IIntegrationSettingsFactory settingsFactory,
    ILibrarySnapshotStore librarySnapshots,
    IRecommendationRunService recommendations,
    ILibraryLinkSuggestionRunService linkSuggestions,
    IAuditTrail auditTrail,
    IIntegrationMapper mapper,
    TimeProvider time,
    ILogger<IntegrationConnectionService> logger) : IIntegrationConnectionService
{
    /// <summary>
    /// How much of an unknown adapter type a sentence repeats: every registered id fits in 32
    /// characters, and a pasted page of text shouldn't come back whole.
    /// </summary>
    private const int AdapterTypeEchoLength = 64;

    /// <summary>How much of an unknown kind a sentence repeats; the longest kind is 14 characters
    /// (<c>DownloadClient</c>; MAM-33's <c>Metadata</c> is shorter).</summary>
    private const int KindEchoLength = 32;

    private const string AdapterTypeMissingError = "Choose the service to connect to.";
    private const string SecretModeError = "A secret change must be Keep, Set or Clear.";

    public async Task<IntegrationListResult> ListAsync(string? search, string? kind, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return IntegrationListResult.Invalid(pagingError);

        var integrations = db.IntegrationConnections.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (ParseKind(kind) is not { } parsedKind)
            {
                return IntegrationListResult.Invalid(
                    $"“{Echo(kind.Trim(), KindEchoLength)}” is not a kind of integration. Use MediaLibrary, ReleaseSearch, DownloadClient or Metadata.");
            }

            // On the column copied from the type at creation, so the filter runs in SQL.
            integrations = integrations.Where(integration => integration.Kind == parsedKind);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Matched against the normalised name, so the search ignores letter case — Cyrillic too,
            // as the upper-casing happens in .NET, not in SQLite, whose upper() knows ASCII only.
            // Contains becomes instr(), which reads '%' and '_' as themselves: a search is text, not
            // a LIKE pattern.
            var normalizedSearch = IntegrationConnectionRules.NormalizeName(search);
            integrations = integrations.Where(integration => integration.NormalizedName.Contains(normalizedSearch));
        }

        var totalCount = await integrations.CountAsync(cancellationToken);
        // Past the last page there is nothing to read; and a page far enough out would overflow the
        // offset, which EF Core takes as an int (the UserAccountService guard).
        var offset = (long)(page - 1) * pageSize;
        List<IntegrationConnectionEntity> rows = offset >= totalCount
            ? []
            : await integrations
                .Include(integration => integration.PathMappings)
                // The normalised name is unique, so the order is total and no integration shows on
                // two pages.
                .OrderBy(integration => integration.NormalizedName)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

        var items = rows.Select(ToDto).ToList();
        return IntegrationListResult.Listed(new IntegrationListResponse(items, totalCount, page, pageSize));
    }

    public IntegrationAdapterTypeListResponse ListAdapterTypes() =>
        new([.. adapters.Descriptors.Select(descriptor => mapper.ToDto(descriptor))]);

    public async Task<IntegrationDto?> GetAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        var integration = await db.IntegrationConnections
            .AsNoTracking()
            .Include(connection => connection.PathMappings)
            .SingleOrDefaultAsync(connection => connection.Id == integrationId, cancellationToken);
        return integration is null ? null : ToDto(integration);
    }

    public async Task<IntegrationChangeResult> CreateAsync(CreateIntegrationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // MAM-7 — checked in this order: type, name, address, user name, secrets, credentials, path
        // mappings, then whether the name is free — one sentence per answer, the first rule broken.
        var (adapter, adapterError) = FindRequestedAdapter(request.AdapterType);
        if (adapter is null)
            return IntegrationChangeResult.Invalid(adapterError!);
        var descriptor = adapter.Descriptor;
        if (IntegrationConnectionRules.ValidateName(request.Name) is { } nameError)
            return IntegrationChangeResult.Invalid(nameError);
        var password = SecretChange.From(request.Password);
        var apiKey = SecretChange.From(request.ApiKey);
        if (ValidateConnection(descriptor, request.BaseUrl, request.UserName, password, apiKey, stored: null) is { } connectionError)
            return IntegrationChangeResult.Invalid(connectionError);
        var pathMappings = ToPairs(request.PathMappings ?? []);
        if (PathMappingRules.Validate(pathMappings, descriptor) is { } pathMappingError)
            return IntegrationChangeResult.Invalid(pathMappingError);

        var name = request.Name!.Trim();
        var normalizedName = IntegrationConnectionRules.NormalizeName(name);
        if (await db.IntegrationConnections.AnyAsync(existing => existing.NormalizedName == normalizedName, cancellationToken))
            return NameTaken(name);

        var actor = await ActorAsync(actorUserId, cancellationToken);
        var integration = new IntegrationConnectionEntity
        {
            Name = name,
            NormalizedName = normalizedName,
            AdapterType = descriptor.TypeId,
            // Copied, so the list filters by kind in SQL, and the row still says what it was should a
            // later build stop registering the type.
            Kind = descriptor.Kind,
            IsEnabled = request.IsEnabled ?? true,
            BaseUrl = IntegrationConnectionRules.NormalizeBaseUrl(request.BaseUrl!),
            UserName = NormalizeUserName(request.UserName),
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        // The row's id exists from construction, so each secret is sealed for this row and field
        // before the row's first save (ADR 0007).
        integration.PasswordEncrypted = Seal(password, stored: null, integration.Id, IntegrationSecretField.Password);
        integration.ApiKeyEncrypted = Seal(apiKey, stored: null, integration.Id, IntegrationSecretField.ApiKey);
        integration.PathMappings = ToEntities(integration.Id, PathMappingRules.Normalize(pathMappings));
        // Add marks the whole graph as new, the path mappings included.
        db.IntegrationConnections.Add(integration);
        auditTrail.Record(AuditAction.IntegrationCreated, actor, AuditTarget.Integration(integration));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // MAM-7 — the MAM-4 race: two adds of one name can both pass the check above; the unique
            // index on the normalised name lets only one commit, and this one lost. Anything else (a
            // full disk) is not a conflict and stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.IntegrationConnections.AnyAsync(existing => existing.NormalizedName == normalizedName, cancellationToken))
                throw;

            logger.LogInformation(exception, "A concurrent request added an integration named {IntegrationName} first; this one changed nothing.", name);
            return NameTaken(name);
        }

        logger.LogInformation(
            "{ActorUserName} added the integration {IntegrationName} ({AdapterType}).", actor.UserName, integration.Name, integration.AdapterType);
        if (integration.Kind == IntegrationKind.Metadata && integration.IsEnabled)
        {
            await QueueRecommendationsAsync(integration.Id, cancellationToken);
            await QueueLinkSuggestionsAsync(integration.Id, cancellationToken);
        }
        return IntegrationChangeResult.Succeeded(mapper.ToDto(integration, descriptor));
    }

    public async Task<IntegrationChangeResult> UpdateAsync(Guid integrationId, UpdateIntegrationRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var integration = await db.IntegrationConnections
            .AsTracking()
            .Include(connection => connection.PathMappings)
            .SingleOrDefaultAsync(connection => connection.Id == integrationId, cancellationToken);
        if (integration is null)
            return IntegrationChangeResult.NotFound;
        // The body is fine; the stored row conflicts with this build. Its settings can't be checked
        // against a type nothing describes any more, so removing it is all that is left.
        if (adapters.Find(integration.AdapterType) is not { } adapter)
            return IntegrationChangeResult.AdapterUnavailable(AdapterUnavailableError(integration));
        var descriptor = adapter.Descriptor;

        // The create order from the name on, against the state the save would leave: a kept secret
        // counts as there when one is stored.
        if (IntegrationConnectionRules.ValidateName(request.Name) is { } nameError)
            return IntegrationChangeResult.Invalid(nameError);
        var password = SecretChange.From(request.Password);
        var apiKey = SecretChange.From(request.ApiKey);
        if (ValidateConnection(descriptor, request.BaseUrl, request.UserName, password, apiKey, integration) is { } connectionError)
            return IntegrationChangeResult.Invalid(connectionError);
        // Left out keeps the stored mappings, so a client that doesn't know them can't delete them.
        var pathMappings = request.PathMappings is null ? null : ToPairs(request.PathMappings);
        if (pathMappings is not null && PathMappingRules.Validate(pathMappings, descriptor) is { } pathMappingError)
            return IntegrationChangeResult.Invalid(pathMappingError);

        var name = request.Name!.Trim();
        var normalizedName = IntegrationConnectionRules.NormalizeName(name);
        if (await db.IntegrationConnections.AnyAsync(other => other.NormalizedName == normalizedName && other.Id != integration.Id, cancellationToken))
            return NameTaken(name);

        var wasEnabled = integration.IsEnabled;
        integration.Name = name;
        integration.NormalizedName = normalizedName;
        integration.BaseUrl = IntegrationConnectionRules.NormalizeBaseUrl(request.BaseUrl!);
        integration.UserName = NormalizeUserName(request.UserName);
        // Left out keeps the switch: a client that doesn't know it must not switch an integration back on.
        if (request.IsEnabled is { } isEnabled)
            integration.IsEnabled = isEnabled;
        integration.PasswordEncrypted = Seal(password, integration.PasswordEncrypted, integration.Id, IntegrationSecretField.Password);
        integration.ApiKeyEncrypted = Seal(apiKey, integration.ApiKeyEncrypted, integration.Id, IntegrationSecretField.ApiKey);
        if (pathMappings is not null)
            ReplacePathMappings(integration, PathMappingRules.Normalize(pathMappings));

        // MAM-4's rule: a save that changes nothing records nothing. Asked before the audit row is
        // added, which is a change itself. A Set always counts: a fresh nonce seals even the same
        // secret as a new value.
        if (!db.ChangeTracker.HasChanges())
            return IntegrationChangeResult.Succeeded();

        var actor = await ActorAsync(actorUserId, cancellationToken);
        auditTrail.Record(AuditAction.IntegrationUpdated, actor, AuditTarget.Integration(integration));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by another request since it was read: nothing of this one was saved.
            db.ChangeTracker.Clear();
            return IntegrationChangeResult.NotFound;
        }
        catch (DbUpdateException exception)
        {
            // The create race, renaming: another integration took the name between the check and the
            // save. Anything else stays a failure.
            db.ChangeTracker.Clear();
            if (!await db.IntegrationConnections.AnyAsync(other => other.NormalizedName == normalizedName && other.Id != integrationId, cancellationToken))
                throw;

            logger.LogInformation(exception, "A concurrent request gave another integration the name {IntegrationName} first; this change was not saved.", name);
            return NameTaken(name);
        }

        logger.LogInformation("{ActorUserName} changed the integration {IntegrationName}.", actor.UserName, integration.Name);
        if (integration.Kind == IntegrationKind.Metadata && integration.IsEnabled && !wasEnabled)
        {
            await QueueRecommendationsAsync(integration.Id, cancellationToken);
            await QueueLinkSuggestionsAsync(integration.Id, cancellationToken);
        }
        return IntegrationChangeResult.Succeeded();
    }

    public async Task<IntegrationChangeResult> DeleteAsync(Guid integrationId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        // Whether its type is still registered doesn't matter: removing is all an integration this
        // build can't connect to is good for.
        var integration = await db.IntegrationConnections
            .AsTracking()
            .SingleOrDefaultAsync(connection => connection.Id == integrationId, cancellationToken);
        if (integration is null)
            return IntegrationChangeResult.NotFound;

        var actor = await ActorAsync(actorUserId, cancellationToken);
        // MAM-8 — one transaction for the purge and the removal: a library whose snapshot is gone but
        // whose row stayed (or the other way round) would be a state nobody asked for. Disposed without
        // a commit, it rolls both back.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // A library's snapshot goes with it (ADR 0006, MAM-8): the titles, identifiers, versions, episode
        // positions (MAM-32), files and libraries it reported, and the works left with nothing. Only its
        // own: every other integration's facts, and anything a non-library source said, stay.
        await librarySnapshots.PurgeAsync(integration.Id, cancellationToken);
        // The audit row copies the name: it has to say what was removed after the row is gone.
        auditTrail.Record(AuditAction.IntegrationDeleted, actor, AuditTarget.Integration(integration));
        // A tracked delete rather than ExecuteDelete, so it commits in the same save as its audit row.
        // The database does the rest (ADR 0006): the path and destination mappings cascade, and so do
        // the tracker sections mapped to destinations when it is a release source (MAM-32); the facts
        // a non-library source reported, the download jobs sent to it and its library sync runs stay,
        // their link set to null and its name kept.
        db.IntegrationConnections.Remove(integration);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Removed by another request since it was read (a double click): gone either way, and that
            // request recorded it. Nothing of this one was saved; the transaction rolls back unused.
            db.ChangeTracker.Clear();
            return IntegrationChangeResult.NotFound;
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("{ActorUserName} removed the integration {IntegrationName}.", actor.UserName, integration.Name);
        return IntegrationChangeResult.Succeeded();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MAM-33 — with a metadata catalog just connected, or switched back on, the recommendations can be computed (again)
    /// without waiting for the next library sync: a computation is queued (or the waiting one joined). The save has happened and stands: a full queue or a failure here is
    /// logged, never the save's answer, and the queuing writes no audit row of its own — the run row records it.
    /// </summary>
    private async Task QueueRecommendationsAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        try
        {
            await recommendations.EnsureQueuedAsync(RecommendationRunTrigger.CatalogConnected, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Metadata integration {IntegrationId} was saved, but no recommendation run could be queued after it.", integrationId);
        }
    }

    /// <summary>
    /// MAM-34 — with a metadata catalog just connected, or switched back on, the library works without a TMDb id can get
    /// their TMDb suggestions without waiting for the next library sync: a preparation is queued (or the waiting one
    /// joined), as the recommendations' run is. The save has happened and stands: a failure here is logged, never the
    /// save's answer.
    /// </summary>
    private async Task QueueLinkSuggestionsAsync(Guid integrationId, CancellationToken cancellationToken)
    {
        try
        {
            await linkSuggestions.EnsureQueuedAsync(LibraryLinkSuggestionRunTrigger.CatalogConnected, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Metadata integration {IntegrationId} was saved, but no TMDb suggestion run could be queued after it.", integrationId);
        }
    }

    private IntegrationDto ToDto(IntegrationConnectionEntity integration) =>
        mapper.ToDto(integration, adapters.Find(integration.AdapterType)?.Descriptor);

    // The adapter a new integration or a new draft names, or the sentence saying why there is none.
    // An exact match: the id is stored on the row and compared byte for byte (ADR 0005).
    private (IIntegrationAdapter? Adapter, string? Error) FindRequestedAdapter(string? adapterType)
    {
        var typeId = adapterType?.Trim() ?? "";
        if (typeId.Length == 0)
            return (null, AdapterTypeMissingError);

        return adapters.Find(typeId) is { } adapter
            ? (adapter, null)
            : (null, $"“{Echo(typeId, AdapterTypeEchoLength)}” is not a service this app can connect to.");
    }

    // The rules a save and a draft test share, in CreateAsync's order: the address, the user name, each
    // secret change, then the credentials the changes leave — on the effective state, so a kept secret
    // counts as there when the stored row has one.
    private static string? ValidateConnection(
        IntegrationAdapterDescriptor adapter,
        string? baseUrl,
        string? userName,
        SecretChange password,
        SecretChange apiKey,
        IntegrationConnectionEntity? stored)
    {
        if (IntegrationConnectionRules.ValidateBaseUrl(baseUrl) is { } baseUrlError)
            return baseUrlError;
        if (IntegrationConnectionRules.ValidateUserName(userName, adapter) is { } userNameError)
            return userNameError;
        // A number outside the enum binds as that number rather than failing the body.
        if (!Enum.IsDefined(password.Mode) || !Enum.IsDefined(apiKey.Mode))
            return SecretModeError;
        if (password.Mode == SecretUpsertMode.Set && IntegrationSecretRules.ValidateNewPassword(password.Value) is { } passwordError)
            return passwordError;
        if (apiKey.Mode == SecretUpsertMode.Set && IntegrationSecretRules.ValidateNewApiKey(apiKey.Value) is { } apiKeyError)
            return apiKeyError;

        return IntegrationConnectionRules.ValidateCredentials(
            adapter,
            hasUserName: NormalizeUserName(userName) is not null,
            hasPassword: password.IsPresentWith(stored?.PasswordEncrypted),
            hasApiKey: apiKey.IsPresentWith(stored?.ApiKeyEncrypted));
    }

    // What a column holds after the change. Keep leaves it byte for byte and never decrypts it: a value
    // sealed under a key that is lost now may be readable again once the key comes back from a backup
    // (ADR 0007).
    private string? Seal(SecretChange change, string? stored, Guid integrationId, IntegrationSecretField field) => change.Mode switch
    {
        SecretUpsertMode.Set => encryption.Encrypt(change.NewSecret(field), IntegrationSecretPurpose.For(integrationId, field)),
        SecretUpsertMode.Clear => null,
        _ => stored,
    };

    // Only when the mappings differ from the stored ones, position by position: replacing equal ones
    // would count as a change and record an audit row for a save that changed nothing.
    private void ReplacePathMappings(IntegrationConnectionEntity integration, IReadOnlyList<PathMappingPair> pathMappings)
    {
        var stored = integration.PathMappings
            .OrderBy(mapping => mapping.Position)
            .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix));
        if (stored.SequenceEqual(pathMappings))
            return;

        // Explicit states rather than navigation fix-up: a new mapping's id is set when it is
        // constructed, and EF Core takes a child it discovers with its key already set for an existing
        // row, updating it instead of inserting it. EF Core orders the deletes before the inserts that
        // reuse a service prefix, so the unique index never sees both at once
        // (Update_replaces_every_path_mapping pins it).
        db.PathMappings.RemoveRange(integration.PathMappings);
        db.PathMappings.AddRange(ToEntities(integration.Id, pathMappings));
    }

    // Read from the database rather than the token: the audit row carries the name the account has.
    private Task<AuditActor> ActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    private static IntegrationChangeResult NameTaken(string name) =>
        IntegrationChangeResult.NameTaken($"An integration named “{name}” already exists.");

    private static string AdapterUnavailableError(IntegrationConnectionEntity integration) =>
        $"This version of the app can't connect to “{integration.AdapterType}”, so this integration can only be removed.";

    // Trimmed; blank is none — the rules read it the same way.
    private static string? NormalizeUserName(string? userName) =>
        string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();

    // As typed, a missing folder read as blank: the rules trim, and name a blank one.
    private static List<PathMappingPair> ToPairs(IReadOnlyList<PathMappingRequest> mappings) =>
        [.. mappings.Select(mapping => new PathMappingPair(mapping?.ServicePrefix ?? "", mapping?.AppPrefix ?? ""))];

    private static List<PathMappingEntity> ToEntities(Guid integrationId, IReadOnlyList<PathMappingPair> pathMappings) =>
        [.. pathMappings.Select((pair, position) => new PathMappingEntity
        {
            IntegrationConnectionId = integrationId,
            Position = position,
            ServicePrefix = pair.ServicePrefix,
            AppPrefix = pair.AppPrefix,
        })];

    // By name only, in any letter case: Enum.TryParse would also take "1", "-1" and
    // "MediaLibrary, ReleaseSearch", which it ORs into DownloadClient.
    private static IntegrationKind? ParseKind(string kind) =>
        Enum.GetNames<IntegrationKind>().FirstOrDefault(name => name.Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<IntegrationKind>(name)
            : null;

    private static string Echo(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : string.Concat(value.AsSpan(0, maximumLength), "…");

    /// <summary>One secret change of a request, a left-out one read as Keep.</summary>
    private sealed record SecretChange(SecretUpsertMode Mode, string? Value)
    {
        public static SecretChange Keep { get; } = new(SecretUpsertMode.Keep, null);

        // Omitted or null is Keep, so a PUT that leaves a secret out keeps it. An undefined mode is
        // kept as it came, for the rules to refuse with their sentence.
        public static SecretChange From(SecretValueUpsert? upsert) => upsert is null ? Keep : new(upsert.Mode, upsert.Value);

        // Whether the secret is there once the change is applied to what is stored.
        public bool IsPresentWith(string? stored) => Mode switch
        {
            SecretUpsertMode.Set => true,
            SecretUpsertMode.Clear => false,
            _ => stored is not null,
        };

        // The value a Set seals and sends: an API key trimmed, as it is pasted and a pasted line break
        // is never part of it; a password as typed, as every character of it counts.
        public string NewSecret(IntegrationSecretField field) =>
            field == IntegrationSecretField.ApiKey ? Value!.Trim() : Value!;

        // MAM-7 — a record's generated ToString would print the new secret into the first log line or
        // debugger view that shows the change.
        public override string ToString() => $"SecretChange {{ Mode = {Mode} }}";
    }
}
