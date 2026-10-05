namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The last step of a file inspection (MAM-19, ADR 0022): hashing the files a byte-identical copy
/// could hide among.</summary>
/// <param name="Candidates">How many files need a hash: files whose size a file of another identity shares
/// and whose media facts don't tell them apart, each counted once however many names lead to it;
/// <c>null</c> until the step has listed them.</param>
/// <param name="Checked">How many of them it has dealt with so far.</param>
/// <param name="Read">How many of them it read in full in this run; the others were answered from the cache
/// or couldn't be hashed.</param>
/// <param name="ProblemCount">How many of them it couldn't hash; never a failure of the run.</param>
/// <param name="Problem">Why the first of them wasn't hashed. For the installation owner only: <c>null</c>
/// for everyone else, and when there was none.</param>
public sealed record FileHashingDto(int? Candidates, int Checked, int Read, int ProblemCount, string? Problem);
