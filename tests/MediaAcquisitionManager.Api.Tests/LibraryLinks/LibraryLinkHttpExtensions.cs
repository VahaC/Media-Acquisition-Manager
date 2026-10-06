using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.LibraryLinks;

/// <summary>
/// MAM-34 — what the TMDb links' HTTP tests share: running the background writer that identifies the queued links (the test
/// host runs no hosted services), and reading the persisted links, their audit rows and the dialog a request answered.
/// </summary>
internal static class LibraryLinkHttpExtensions
{
    /// <summary>
    /// Runs the background writer until it has identified every link queued right now: a Set, Replace, Retry, "Accept all
    /// confident" click or the repair only queues (no request waits for the library), so a test that wants the outcome runs
    /// the worker, exactly as the app's hosted service would have between two polls.
    /// </summary>
    public static Task IdentifyQueuedLinksAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken) =>
        factory.Resolve<LibraryLinkWriteWorker>().RunQueuedAsync(cancellationToken);

    /// <summary>The links of the database, every state, in no particular order.</summary>
    public static async Task<List<LibraryItemLinkEntity>> LinksAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken)
    {
        var links = new List<LibraryItemLinkEntity>();
        await factory.WithDbAsync(async db => links.AddRange(await db.LibraryItemLinks.AsNoTracking().ToListAsync(cancellationToken)));
        return links;
    }

    /// <summary>The one link of the database.</summary>
    public static async Task<LibraryItemLinkEntity> SingleLinkAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken)
    {
        LibraryItemLinkEntity? link = null;
        await factory.WithDbAsync(async db => link = await db.LibraryItemLinks.AsNoTracking().SingleAsync(cancellationToken));
        return link!;
    }

    /// <summary>The work the library item <paramref name="itemId"/> sits on.</summary>
    public static async Task<Guid> WorkOfItemAsync(this MediaAcquisitionManagerApiFactory factory, string itemId, CancellationToken cancellationToken)
    {
        var workId = Guid.Empty;
        await factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .AsNoTracking()
            .Where(title => title.SourceItemId == itemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync(cancellationToken));
        return workId;
    }

    /// <summary>
    /// The audit rows in the order they were recorded (time, then the action's number: one request records a set and an
    /// identification in one instant), each with its target and its ids as "Role Scheme Value" in role and scheme order.
    /// </summary>
    public static async Task<List<(AuditAction Action, string? ActorUserName, AuditTargetKind? TargetKind, Guid? TargetId, string? TargetName, string Identifiers)>> AuditAsync(
        this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken)
    {
        var rows = new List<(AuditAction, string?, AuditTargetKind?, Guid?, string?, string)>();
        await factory.WithDbAsync(async db =>
        {
            var events = (await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(auditEvent => auditEvent.CreatedUtc)
                .ThenBy(auditEvent => auditEvent.Action)
                .ToList();
            var identifiers = (await db.AuditEventIdentifiers.AsNoTracking().ToListAsync(cancellationToken)).ToLookup(identifier => identifier.AuditEventId);
            rows.AddRange(events.Select(auditEvent => (
                auditEvent.Action,
                auditEvent.ActorUserName,
                auditEvent.TargetKind,
                auditEvent.TargetId,
                auditEvent.TargetName,
                string.Join(", ", identifiers[auditEvent.Id]
                    .OrderBy(identifier => identifier.Role)
                    .ThenBy(identifier => identifier.Scheme)
                    .Select(identifier => $"{identifier.Role} {identifier.Scheme} {identifier.Value}")))));
        });
        return rows;
    }

    /// <summary>Only the actions of the audit rows, in the order they were recorded.</summary>
    public static async Task<List<AuditAction>> AuditActionsAsync(this MediaAcquisitionManagerApiFactory factory, CancellationToken cancellationToken) =>
        [.. (await factory.AuditAsync(cancellationToken)).Select(row => row.Action)];

    /// <summary>What a response carries, deserialized as the app's own JSON does.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    /// <summary>The dialog a request answered.</summary>
    public static Task<LibraryLinkDialogResponse> ReadDialogAsync(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.ReadAsync<LibraryLinkDialogResponse>(cancellationToken);
}
