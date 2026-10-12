namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-40 — one page of the removals still open under Removed copies, and the room they take in the
/// quarantine.</summary>
/// <param name="Items">The removals on this page, newest first: the ones still moving, in the quarantine, waiting for
/// review, or being put back or deleted. The ended ones are history and aren't listed.</param>
/// <param name="TotalCount">Every open removal, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
/// <param name="QuarantinedBytes">The sizes of every open removal's names in the quarantine, added up.</param>
/// <param name="SwitchedOff">Replacing and removing copies is switched off on the Media folders page right now: nothing is
/// put back or deleted while it is off (ADR 0033), so the page disables Restore and Delete for good and says why — as a
/// replacement's page does with its own <c>switchedOff</c>.</param>
public sealed record CopyRemovalListResponse(
    IReadOnlyList<CopyRemovalDto> Items, int TotalCount, int Page, int PageSize, long QuarantinedBytes, bool SwitchedOff);
