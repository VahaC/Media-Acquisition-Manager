using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-14 — what the list of a user's download jobs may be filtered by. The rule answers why a value is refused, as a
/// message the Api renders (ADR 0042), or <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern —
/// and reads the value into the status to filter by. Paging is <c>PagingRules</c>', as on every other list. MAM-41 —
/// and the Jobs page's tab, the same way.
/// </summary>
public static class DownloadJobListRules
{
    /// <summary>How much of a value that isn't a status a message repeats, as the other list rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// Why <paramref name="status"/> is not a download status, or <c>null</c> with the status
    /// it names in <paramref name="filter"/> — <c>null</c> for a blank one, which filters nothing.
    /// </summary>
    /// <param name="status">A status by its name, in any letter case, as a query string carries it
    /// (<c>downloading</c>); surrounding whitespace is ignored.</param>
    /// <param name="filter">The status named, when it is one.</param>
    /// <remarks>
    /// By name only: <c>Enum.TryParse</c> would also take <c>3</c>, <c>-1</c> and <c>Created, Failed</c>,
    /// which it ORs into one value — the <c>IntegrationConnectionService</c> rule for an integration's kind.
    /// </remarks>
    public static Message? Validate(string? status, out DownloadJobStatus? filter)
    {
        filter = null;
        if (string.IsNullOrWhiteSpace(status))
            return null;

        var text = status.Trim();
        if (ByName<DownloadJobStatus>(text) is not { } named)
            return Message.Of("downloads.jobs.status-unknown", ("value", Echo(text)));

        filter = named;
        return null;
    }

    /// <summary>
    /// MAM-41 — why <paramref name="view"/> is not one of the Jobs page's tabs, or <c>null</c> with the view
    /// it names in <paramref name="filter"/> — <c>null</c> for a blank one, which lists every job. Read as a status is: by
    /// name only, in any letter case, surrounding whitespace ignored.
    /// </summary>
    public static Message? ValidateView(string? view, out DownloadJobView? filter)
    {
        filter = null;
        if (string.IsNullOrWhiteSpace(view))
            return null;

        var text = view.Trim();
        if (ByName<DownloadJobView>(text) is not { } named)
            return Message.Of("downloads.jobs.view-unknown", ("value", Echo(text)));

        filter = named;
        return null;
    }

    // By name only (see Validate): a number or a comma-separated list is no name.
    private static TEnum? ByName<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().FirstOrDefault(name => name.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<TEnum>(name)
            : null;

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
