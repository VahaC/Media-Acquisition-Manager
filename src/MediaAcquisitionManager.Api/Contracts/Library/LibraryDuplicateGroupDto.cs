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
/// <param name="Decision">MAM-21 — the owner's decision on a possible match, kept by <paramref name="Key"/>; MAM-40 — or
/// on copies of one work or byte-identical copies kept as they are (<c>Dismissed</c>), while no new file has joined them;
/// <c>null</c> while undecided, and always for one file's names. It changes neither <paramref name="NeedsReview"/> nor
/// what the group is (ADR 0029).</param>
public sealed record LibraryDuplicateGroupDto(
    string Key,
    DuplicateClass Class,
    bool NeedsReview,
    IReadOnlyList<DuplicateDistinctionDto> Distinctions,
    DuplicateEvidenceDto Evidence,
    IReadOnlyList<LibraryDuplicateFileDto> Files,
    LibrarySpaceDto Space,
    DuplicateRecoverableSpaceDto? Recoverable,
    DuplicateDecisionDto? Decision);
