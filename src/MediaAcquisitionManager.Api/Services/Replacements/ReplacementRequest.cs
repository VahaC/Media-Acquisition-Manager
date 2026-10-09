namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the replacement part of a schedule (ADR 0032): the copies the owner ticked and the title-and-year matches he
/// confirmed, by id. What a retry under the same request id must ask for again to be answered with the job it made
/// (ADR 0013).
/// </summary>
/// <param name="FileIds">The ticked copies' file ids, as the Download dialog's read listed them.</param>
/// <param name="ConfirmedWorkIds">The works whose "This release is …" the owner ticked.</param>
public sealed record ReplacementRequest(IReadOnlyList<Guid> FileIds, IReadOnlyList<Guid> ConfirmedWorkIds)
{
    /// <summary>
    /// Whether a replacement scheduled with <paramref name="fileIds"/> and <paramref name="confirmedWorkIds"/> is the one
    /// this asks for. As sets: the dialog's order of ticks is no part of what was asked, and a list read again after a
    /// refresh may come back in another order.
    /// </summary>
    public bool IsSameAs(IEnumerable<Guid> fileIds, IEnumerable<Guid> confirmedWorkIds) =>
        FileIds.ToHashSet().SetEquals(fileIds) && ConfirmedWorkIds.ToHashSet().SetEquals(confirmedWorkIds);
}
