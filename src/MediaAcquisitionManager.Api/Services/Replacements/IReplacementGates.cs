namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the owner's gates on replacing copies (ADR 0033): the master switch, off by default, and a tick per library
/// folder, none by default, read against the allowed roots and the library folders the app knows. One read for the
/// Media folders page, the Download dialog and the send, so none of them can judge a folder differently.
/// </summary>
public interface IReplacementGates
{
    /// <summary>
    /// The gates as stored right now. Read fresh on every call and never cached, the rule for settings that gate a
    /// write: a Save of the Media folders page counts at the next read.
    /// </summary>
    Task<ReplacementGateState> ReadAsync(CancellationToken cancellationToken = default);
}
