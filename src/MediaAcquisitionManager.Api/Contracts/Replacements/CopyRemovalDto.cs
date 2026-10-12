using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-40 — one removal of a copy as its owner sees it under Removed copies (ADR 0030's quarantine): where it stands, the
/// copy it took out of its group, where its video went, the room its names take and what Delete for good frees, and which
/// of the owner's actions are offered.
/// </summary>
/// <param name="Id">The removal.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Reason">Why it waits for review, or why it didn't move, the app's own sentence; <c>null</c> otherwise.</param>
/// <param name="Title">The group's heading when it was removed: "The Gods Must Be Crazy (1980)", or an episode's
/// "Clarkson's Farm · S02E03 · Title".</param>
/// <param name="GroupClass">What the group's files were to each other: <c>SameWork</c> or <c>ByteIdentical</c>.</param>
/// <param name="OriginalPath">The video's path inside the app: where Restore puts it back.</param>
/// <param name="QuarantinePath">The video's path in the quarantine.</param>
/// <param name="SizeBytes">The sizes of its names in the quarantine, added up.</param>
/// <param name="FreesBytes">The room Delete for good would free: the names in the quarantine with no other name.</param>
/// <param name="RequestedByUserName">Who removed it.</param>
/// <param name="CreatedUtc">When it was removed.</param>
/// <param name="StateChangedUtc">When its state last changed.</param>
/// <param name="CanRestore">Some of its names are in the quarantine and its video wasn't deleted for good, so Restore puts
/// them back.</param>
/// <param name="CanDeleteForGood">Some of its names are in the quarantine and no write is under way, so Delete for good
/// may remove them.</param>
/// <param name="Names">Every name of its plan, in order: the video, then its companions — the shape of a replacement's
/// names (<see cref="ReplacementNameDto"/>).</param>
public sealed record CopyRemovalDto(
    Guid Id,
    CopyRemovalState State,
    string? Reason,
    string Title,
    DuplicateClass GroupClass,
    string OriginalPath,
    string QuarantinePath,
    long SizeBytes,
    long FreesBytes,
    string RequestedByUserName,
    DateTime CreatedUtc,
    DateTime StateChangedUtc,
    bool CanRestore,
    bool CanDeleteForGood,
    IReadOnlyList<ReplacementNameDto> Names);
