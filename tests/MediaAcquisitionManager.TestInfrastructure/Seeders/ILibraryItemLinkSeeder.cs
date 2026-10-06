using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-34 — library items' TMDb links for a test: links the owner set before it started.</summary>
public interface ILibraryItemLinkSeeder
{
    /// <summary>
    /// A link of the item <paramref name="sourceItemId"/> of <paramref name="integration"/> to the TMDb film or show
    /// <paramref name="value"/>, sitting on <paramref name="mediaWorkId"/>, set by <paramref name="setBy"/> (the owner's
    /// name "owner" when <c>null</c>), with its files' identities. <paramref name="configure"/> sets any other field.
    /// </summary>
    Task<LibraryItemLinkEntity> Create(
        IntegrationConnectionEntity integration,
        string sourceItemId,
        Guid? mediaWorkId,
        MediaIdentifierScheme scheme,
        string value,
        LibraryLinkTarget target = LibraryLinkTarget.Work,
        LibraryLinkWriteState writeState = LibraryLinkWriteState.KeptInApp,
        UserEntity? setBy = null,
        IReadOnlyList<(long DeviceId, long Inode, long SizeBytes)>? files = null,
        Action<LibraryItemLinkEntity>? configure = null);

    /// <summary>One more file identity of the link <paramref name="linkId"/>.</summary>
    Task<LibraryItemLinkFileEntity> AddFile(Guid linkId, long deviceId, long inode, long sizeBytes);
}
