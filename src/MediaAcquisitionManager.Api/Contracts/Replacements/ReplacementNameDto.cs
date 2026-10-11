using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-26 — one name of a replacement's plan, as the job's page lists it (ADR 0030).</summary>
/// <param name="Id">The name's row: what Delete for good confirms it by.</param>
/// <param name="Kind">The ticked copy's video, or one of its companions.</param>
/// <param name="OriginalPath">Where it was in the library, inside the app: where Restore puts it back.</param>
/// <param name="QuarantinePath">Where it goes in the quarantine.</param>
/// <param name="SizeBytes">Its size before the move.</param>
/// <param name="OtherNames">The file has another name (a hardlink), so deleting it frees no space.</param>
/// <param name="State">Where it is now.</param>
/// <param name="Problem">Why it stayed, wasn't put back or deleted, or was lost; <c>null</c> otherwise.</param>
public sealed record ReplacementNameDto(
    Guid Id,
    ReplacementNameKind Kind,
    string OriginalPath,
    string QuarantinePath,
    long SizeBytes,
    bool OtherNames,
    ReplacementNameState State,
    string? Problem);
