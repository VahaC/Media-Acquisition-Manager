using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>MAM-20 — one group of the Duplicates list, as <see cref="DuplicateGrouping"/> works it out.</summary>
/// <param name="Key">What names the group on every read while its members stay (ADR 0028): 32 lowercase hex
/// characters, from its class and what makes it one — the file, the hash, the works (with the segment letters, years
/// or editions that parted their copies, MAM-37), the subjects.</param>
/// <param name="Class">What its files are to each other.</param>
/// <param name="NeedsReview">Whether the owner must look before anything is done with it: a possible match always,
/// another copy of a work with a distinction.</param>
/// <param name="Distinctions">What tells its files or works apart, each once, by kind, then scheme.</param>
/// <param name="FileIds">The names in it (<see cref="DuplicateName.FileId"/>), each once.</param>
/// <param name="WorkIds">The works it is about: those holding its names, for a possible match the works it
/// offers.</param>
/// <param name="Identity">The file, for <see cref="DuplicateClass.SameFile"/>.</param>
/// <param name="Sha256">The bytes' hash, for <see cref="DuplicateClass.ByteIdentical"/>.</param>
/// <param name="Identifiers">The registry keys that make its works one, for <see cref="DuplicateClass.SameWork"/>: those
/// two or more of them share, or a single work's own.</param>
/// <param name="Titles">The titles and years its subjects share, for <see cref="DuplicateClass.Possible"/>.</param>
/// <param name="DurationsAgree">For <see cref="DuplicateClass.Possible"/>, whether the measured durations of its files
/// agree (<see cref="DurationAgreement"/>); <c>null</c> when fewer than two are known, or for another class.</param>
/// <param name="RunningTimes">MAM-37 — for <see cref="DuplicateClass.SameWork"/>, the running times of its shortest and
/// its longest measured copy, whether or not they differ widely (<see cref="RunningTimeSpread"/>); <c>null</c> when
/// fewer than two copies have one, or for another class.</param>
/// <param name="Recoverable">The room removing the extra copies would free: none for one file's names, an estimate or
/// why there is none for byte-identical copies; <c>null</c> for copies of a work and possible matches, which are never
/// redundant.</param>
public sealed record DuplicateGroup(
    string Key,
    DuplicateClass Class,
    bool NeedsReview,
    IReadOnlyList<DuplicateDistinction> Distinctions,
    IReadOnlyList<Guid> FileIds,
    IReadOnlyList<Guid> WorkIds,
    DuplicateIdentity? Identity,
    string? Sha256,
    IReadOnlyList<RegistryKey> Identifiers,
    IReadOnlyList<DuplicateTitle> Titles,
    bool? DurationsAgree,
    DuplicateRunningTimes? RunningTimes,
    DuplicateRecoverableSpace? Recoverable);
