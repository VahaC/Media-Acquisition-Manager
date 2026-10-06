using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ILibraryItemLinkSeeder"/>
public sealed class LibraryItemLinkSeeder(IServiceProvider services) : SeederBase(services), ILibraryItemLinkSeeder
{
    private ILibraryItemLinkDataFactory Links => new LibraryItemLinkDataFactory(Db, Resolve<TimeProvider>());

    public Task<LibraryItemLinkEntity> Create(
        IntegrationConnectionEntity integration,
        string sourceItemId,
        Guid? mediaWorkId,
        MediaIdentifierScheme scheme,
        string value,
        LibraryLinkTarget target = LibraryLinkTarget.Work,
        LibraryLinkWriteState writeState = LibraryLinkWriteState.KeptInApp,
        UserEntity? setBy = null,
        IReadOnlyList<(long DeviceId, long Inode, long SizeBytes)>? files = null,
        Action<LibraryItemLinkEntity>? configure = null) =>
        Links.Create(integration.Id, sourceItemId, mediaWorkId, scheme, value, target, writeState, setBy?.Id, setBy?.UserName ?? "owner", files, configure);

    public Task<LibraryItemLinkFileEntity> AddFile(Guid linkId, long deviceId, long inode, long sizeBytes) =>
        Links.AddFile(linkId, deviceId, inode, sizeBytes);
}
