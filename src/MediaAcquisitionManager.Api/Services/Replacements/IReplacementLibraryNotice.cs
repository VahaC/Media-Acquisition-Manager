namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — tells the library that a replacement's names left it or came back (ADR 0030: "after every move and every
/// restore the app sends <c>POST /Library/Media/Updated</c> with the paths involved"), and nothing more: the names the
/// quarantine holds as deleted at their old places, the names put back as created, each in the library's own terms,
/// never a quarantine path.
/// </summary>
/// <remarks>
/// <para>
/// A notice is owed while the job's <c>LibraryNoticeDueUtc</c> is set, which the move and the restore set in the same
/// save as their outcome; it is cleared once every library took it, or once the library's wait ran out
/// (<c>ReplacementVerificationRules.LibraryWait</c>) or it can't be told, each logged. A notice never changes the job's
/// state, and a library that doesn't answer keeps it owed for the mover's next pass.
/// </para>
/// <para>
/// Never throws but for the caller's cancellation.
/// </para>
/// </remarks>
public interface IReplacementLibraryNotice
{
    /// <summary>
    /// Sends the notice the replacement <paramref name="replacementJobId"/> owes, if it owes one, and clears it once
    /// settled; nothing when none is owed.
    /// </summary>
    Task SendDueAsync(Guid replacementJobId, CancellationToken cancellationToken = default);
}
