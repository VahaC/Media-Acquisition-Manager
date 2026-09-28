namespace MediaAcquisitionManager.Api.Services.Auth;

/// <summary>The outcome of <see cref="ISessionService.RefreshAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Grant">The rotated session when <see cref="SessionRefreshStatus.Refreshed"/>.</param>
public sealed record SessionRefreshResult(SessionRefreshStatus Status, SessionGrant? Grant)
{
    public static SessionRefreshResult Conflict { get; } = new(SessionRefreshStatus.Conflict, null);

    public static SessionRefreshResult Ended { get; } = new(SessionRefreshStatus.Ended, null);

    public static SessionRefreshResult Refreshed(SessionGrant grant) => new(SessionRefreshStatus.Refreshed, grant);
}
