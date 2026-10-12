namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — tells the library that a removal's names left it or came back (ADR 0030), as a replacement's notice does
/// (<c>IReplacementLibraryNotice</c>): the names the quarantine holds as deleted at their old places, the names put back as
/// created, each in the library's own terms, never a quarantine path. Owed while the removal's
/// <c>LibraryNoticeDueUtc</c> is set; cleared once every library took it, or once the library's wait ran out or it can
/// never be told. Never throws but for the caller's cancellation.
/// </summary>
public interface ICopyRemovalLibraryNotice
{
    /// <summary>Sends the notice the removal <paramref name="copyRemovalId"/> owes, if it owes one, and clears it once
    /// settled; nothing when none is owed.</summary>
    Task SendDueAsync(Guid copyRemovalId, CancellationToken cancellationToken = default);
}
