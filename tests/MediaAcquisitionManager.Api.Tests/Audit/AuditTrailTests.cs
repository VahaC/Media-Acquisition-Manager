using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Audit;

// MAM-34 — the audit trail's ids (R14): a row recorded with the ids a link change found and left adds them to the same
// unsaved row, in their order, so the caller's one save commits both or neither; and a link is named on its row by its
// work's title, cut to the column with an ellipsis (MAM-21 — and a possible match by its group's key, named by its
// title). The trail is resolved from the app's own container, as a service uses it; no endpoint reads it.
public sealed class AuditTrailTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private readonly MediaAcquisitionManagerApiFactory _factory;

    public AuditTrailTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Record_with_identifiers_adds_them_to_the_rows_one_save_in_their_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await new UserSeeder(_factory.Scope).CreateOwner("owner");
        var linkId = Guid.NewGuid();
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var auditTrail = _factory.Scope.GetRequiredService<IAuditTrail>();
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();

        //Act
        auditTrail.Record(
            AuditAction.LibraryIdsWritten,
            AuditActor.From(owner),
            AuditTarget.LibraryLink(linkId, "120. Landing Stripling"),
            [
                new AuditIdentifier(AuditIdentifierRole.Before, MediaIdentifierScheme.Tmdb, "999"),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, "223021"),
                new AuditIdentifier(AuditIdentifierRole.After, MediaIdentifierScheme.Imdb, "tt0056164"),
            ]);
        var beforeSave = await CountsAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        //Assert
        // Nothing reaches the database before the caller's save: the row and its ids commit together.
        Assert.Equal((0, 0), beforeSave);
        await _factory.WithDbAsync(async check =>
        {
            var audit = await check.AuditEvents.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((AuditAction.LibraryIdsWritten, owner.Id, "owner"), (audit.Action, audit.ActorUserId, audit.ActorUserName));
            Assert.Equal((AuditTargetKind.LibraryLink, linkId, "120. Landing Stripling", now), (audit.TargetKind, audit.TargetId, audit.TargetName, audit.CreatedUtc));
            var identifiers = await check.AuditEventIdentifiers.AsNoTracking().OrderBy(identifier => identifier.Position).ToListAsync(cancellationToken);
            Assert.Equal(
            [
                (audit.Id, 0, AuditIdentifierRole.Before, MediaIdentifierScheme.Tmdb, "999"),
                (audit.Id, 1, AuditIdentifierRole.After, MediaIdentifierScheme.Tmdb, "223021"),
                (audit.Id, 2, AuditIdentifierRole.After, MediaIdentifierScheme.Imdb, "tt0056164"),
            ], identifiers.Select(identifier => (identifier.AuditEventId, identifier.Position, identifier.Role, identifier.Scheme, identifier.Value)));
        });
    }

    [Fact]
    public async Task Record_without_identifiers_adds_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var auditTrail = _factory.Scope.GetRequiredService<IAuditTrail>();
        var db = _factory.Scope.GetRequiredService<ApplicationDbContext>();

        //Act
        auditTrail.Record(AuditAction.LibraryLinkRemoved, actor: null, AuditTarget.LibraryLink(Guid.NewGuid(), "Aliens"), []);
        await db.SaveChangesAsync(cancellationToken);

        //Assert
        Assert.Equal((1, 0), await CountsAsync(cancellationToken));
    }

    [Fact]
    public void LibraryLink_cuts_a_long_title_to_the_column_with_an_ellipsis()
    {
        //Arrange
        var title = new string('Ж', 199) + "кінець";

        //Act
        var target = AuditTarget.LibraryLink(Guid.NewGuid(), title);
        var exact = AuditTarget.LibraryLink(Guid.NewGuid(), new string('Ж', 200));

        //Assert
        Assert.Equal((200, new string('Ж', 199) + "…"), (target.Name.Length, target.Name));
        Assert.Equal(new string('Ж', 200), exact.Name);
        Assert.Equal(AuditTargetKind.LibraryLink, target.Kind);
    }

    [Fact]
    public void DuplicateMatch_targets_the_groups_key_as_a_guid_and_cuts_a_long_title_with_an_ellipsis()
    {
        //Arrange
        const string key = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";
        var title = new string('Ж', 199) + "кінець (2016)";

        //Act
        var target = AuditTarget.DuplicateMatch(key, title);
        var plain = AuditTarget.DuplicateMatch(key, "Arrival (2016)");

        //Assert
        // MAM-21 — the key's 32 hex digits are the target's id, and give the key back: every decision on one group has
        // one target, whichever row holds it.
        Assert.Equal((AuditTargetKind.DuplicateMatch, key), (target.Kind, target.Id.ToString("N")));
        Assert.Equal(target.Id, plain.Id);
        Assert.Equal((200, new string('Ж', 199) + "…"), (target.Name.Length, target.Name));
        Assert.Equal("Arrival (2016)", plain.Name);
    }

    private async Task<(int Events, int Identifiers)> CountsAsync(CancellationToken cancellationToken)
    {
        var counts = (0, 0);
        await _factory.WithDbAsync(async db =>
            counts = (await db.AuditEvents.CountAsync(cancellationToken), await db.AuditEventIdentifiers.CountAsync(cancellationToken)));
        return counts;
    }
}
