using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — the room removing all but one copy of a group's file would free (ADR 0018).</summary>
/// <param name="SizeBytes">The bytes freed; <c>null</c> when it can't be told — never a guess.</param>
/// <param name="Gap">Why it can't be told; <c>null</c> when it can.</param>
public sealed record DuplicateRecoverableSpaceDto(long? SizeBytes, RecoverableSpaceGap? Gap);
