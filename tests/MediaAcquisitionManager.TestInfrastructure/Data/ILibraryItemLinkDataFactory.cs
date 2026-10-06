using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-34 — creates library items' TMDb links, and the identities of their files, directly in the
/// database.</summary>
public interface ILibraryItemLinkDataFactory
{
    /// <summary>
    /// A saved link of the item <paramref name="sourceItemId"/> of the library integration
    /// <paramref name="integrationConnectionId"/>, with the identities <paramref name="files"/>. Stored as given — any
    /// state, any value, a work it doesn't sit on — so a test can plant a row the app would never write;
    /// <paramref name="configure"/> sets any field the parameters don't name before it is saved.
    /// </summary>
    /// <remarks>
    /// The item's kind follows the link (a show linked to the work itself: a series; anything else: a film), and every
    /// time is the clock's now, unless <paramref name="configure"/> says otherwise.
    /// </remarks>
    Task<LibraryItemLinkEntity> Create(
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
        Action<LibraryItemLinkEntity>? configure = null);

    /// <summary>A saved identity of one more file of the link <paramref name="linkId"/>.</summary>
    Task<LibraryItemLinkFileEntity> AddFile(Guid linkId, long deviceId, long inode, long sizeBytes);
}
