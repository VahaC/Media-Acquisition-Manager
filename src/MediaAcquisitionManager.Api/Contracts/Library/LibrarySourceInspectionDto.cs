namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Where the app's own look at one source's files stands (MAM-9), counted from the files when
/// asked, so it moves as a run writes them.</summary>
/// <param name="InspectedCount">Files whose streams were read.</param>
/// <param name="ProblemCount">Files the last look couldn't read the streams of, whatever the reason
/// (no path in the app included).</param>
/// <param name="NotInspectedCount">Files the app hasn't looked at yet.</param>
/// <param name="Problem">Why the first of the problem files (by path) wasn't read; for the installation
/// owner only, <c>null</c> for everyone else and when there is none.</param>
public sealed record LibrarySourceInspectionDto(int InspectedCount, int ProblemCount, int NotInspectedCount, string? Problem);
