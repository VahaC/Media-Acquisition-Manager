using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-20 — one group of duplicates (specification §3.3), worked out when read from what the app holds; nothing about
/// it is stored (ADR 0028).
/// </summary>
/// <param name="Key">What names the group on every read while its members stay: 32 lowercase hex characters.</param>
/// <param name="Class">What its files are to each other.</param>
/// <param name="NeedsReview">Whether the owner must look before anything is done with it: a possible match always,
/// copies of one work when a distinction tells them apart.</param>
/// <param name="Distinctions">What tells its files or works apart, by kind, then scheme.</param>
/// <param name="Evidence">Why they are a group: each class fills its part.</param>
/// <param name="Files">Its file names, by service path (ordinal), then id.</param>
/// <param name="Space">The room its files take, each file once (ADR 0018).</param>
/// <param name="Recoverable">The room removing all but one copy would free: 0 for one file's names, an estimate or why
/// there is none for byte-identical files; <c>null</c> for copies of a work and possible matches, which are never
/// redundant.</param>
public sealed record LibraryDuplicateGroupDto(
    string Key,
    DuplicateClass Class,
    bool NeedsReview,
    IReadOnlyList<DuplicateDistinctionDto> Distinctions,
    DuplicateEvidenceDto Evidence,
    IReadOnlyList<LibraryDuplicateFileDto> Files,
    LibrarySpaceDto Space,
    DuplicateRecoverableSpaceDto? Recoverable);
