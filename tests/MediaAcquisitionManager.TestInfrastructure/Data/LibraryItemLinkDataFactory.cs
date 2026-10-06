using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <inheritdoc cref="ILibraryItemLinkDataFactory"/>
/// <remarks>The file identities are the link's own rows, so they are added through the link's factory rather than a
/// factory of their own.</remarks>
public sealed class LibraryItemLinkDataFactory(ApplicationDbContext db, TimeProvider time) : ILibraryItemLinkDataFactory
{
    public async Task<LibraryItemLinkEntity> Create(
        Guid integrationConnectionId,
        string sourceItemId,
        Guid? mediaWorkId,
        MediaIdentifierScheme scheme,
        string value,
        LibraryLinkTarget target = LibraryLinkTarget.Work,
        LibraryLinkWriteState writeState = LibraryLinkWriteState.KeptInApp,
        Guid? setByUserId = null,
        string setByUserName = "owner",
        IReadOnlyList<(long DeviceId, long Inode, long SizeBytes)>? files = null,
        Action<LibraryItemLinkEntity>? configure = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var link = new LibraryItemLinkEntity
        {
            IntegrationConnectionId = integrationConnectionId,
            SourceItemId = sourceItemId,
            ItemKind = target == LibraryLinkTarget.Work && scheme == MediaIdentifierScheme.TmdbTv ? MediaWorkKind.Series : MediaWorkKind.Movie,
            MediaWorkId = mediaWorkId,
            Target = target,
            Scheme = scheme,
            Value = value,
            SetByUserId = setByUserId,
            SetByUserName = setByUserName,
            SetUtc = now,
            WriteState = writeState,
            WriteAttemptId = Guid.NewGuid(),
            WriteStateUtc = now,
            CreatedUtc = now,
        };
        configure?.Invoke(link);

        db.LibraryItemLinks.Add(link);
        db.LibraryItemLinkFiles.AddRange((files ?? []).Select(file => new LibraryItemLinkFileEntity
        {
            LibraryItemLinkId = link.Id,
            DeviceId = file.DeviceId,
            Inode = file.Inode,
            SizeBytes = file.SizeBytes,
        }));
        await db.SaveChangesAsync();
        // Detached, so the test's later reads come from the database, not from this tracked copy.
        db.ChangeTracker.Clear();
        return link;
    }

    public async Task<LibraryItemLinkFileEntity> AddFile(Guid linkId, long deviceId, long inode, long sizeBytes)
    {
        var file = new LibraryItemLinkFileEntity { LibraryItemLinkId = linkId, DeviceId = deviceId, Inode = inode, SizeBytes = sizeBytes };
        db.LibraryItemLinkFiles.Add(file);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return file;
    }
}
