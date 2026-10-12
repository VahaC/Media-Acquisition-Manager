using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates the owner's removals of a duplicate copy and the names they moved, directly in the database (MAM-40): as
/// a previous request left them.</summary>
public interface ICopyRemovalDataFactory
{
    /// <summary>
    /// A saved removal of the copy at <paramref name="appPath"/> (its library's path <paramref name="servicePath"/>) in
    /// <paramref name="state"/>, by <paramref name="requestedByUserId"/> (its name read from the account) at
    /// <paramref name="createdUtc"/> or the factory's now, of the group <paramref name="groupKey"/> headed
    /// <paramref name="title"/>, with <paramref name="reason"/>, and a library notice owed since
    /// <paramref name="libraryNoticeDueUtc"/> when given. No name: each is a row of its own.
    /// </summary>
    Task<CopyRemovalEntity> Create(
        Guid requestedByUserId,
        CopyRemovalState state,
        string appPath,
        string servicePath,
        string title = "Alien (1979)",
        string groupKey = "0123456789abcdef0123456789abcdef",
        DuplicateClass groupClass = DuplicateClass.SameWork,
        string? reason = null,
        DateTime? createdUtc = null,
        DateTime? libraryNoticeDueUtc = null);

    /// <summary>A saved name of the removal <paramref name="copyRemovalId"/> at <paramref name="position"/>, where it was and
    /// where it goes, read with <paramref name="facts"/> (which must carry an identity), in <paramref name="state"/>.</summary>
    Task<CopyRemovalNameEntity> AddName(
        Guid copyRemovalId,
        int position,
        ReplacementNameKind kind,
        string appPath,
        string quarantinePath,
        string allowedRoot,
        FileFacts facts,
        ReplacementNameState state = ReplacementNameState.Planned,
        string? problem = null,
        DateTime? movedUtc = null,
        DateTime? restoredUtc = null,
        DateTime? deletedUtc = null);
}
