using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-14 — what the list of a user's download jobs may be filtered by. The rule answers one readable
/// sentence, or <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern — and reads the value
/// into the status to filter by. Paging is <c>PagingRules</c>', as on every other list.
/// </summary>
public static class DownloadJobListRules
{
    /// <summary>How much of a value that isn't a status a sentence repeats, as the other list rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// The sentence saying <paramref name="status"/> is not a download status, or <c>null</c> with the status
    /// it names in <paramref name="filter"/> — <c>null</c> for a blank one, which filters nothing.
    /// </summary>
    /// <param name="status">A status by its name, in any letter case, as a query string carries it
    /// (<c>downloading</c>); surrounding whitespace is ignored.</param>
    /// <param name="filter">The status named, when it is one.</param>
    /// <remarks>
    /// By name only: <c>Enum.TryParse</c> would also take <c>3</c>, <c>-1</c> and <c>Created, Failed</c>,
    /// which it ORs into one value — the <c>IntegrationConnectionService</c> rule for an integration's kind.
    /// </remarks>
    public static string? Validate(string? status, out DownloadJobStatus? filter)
    {
        filter = null;
        if (string.IsNullOrWhiteSpace(status))
            return null;

        var text = status.Trim();
        if (Enum.GetNames<DownloadJobStatus>().FirstOrDefault(name => name.Equals(text, StringComparison.OrdinalIgnoreCase)) is not { } name)
            return $"“{Echo(text)}” is not a download status. Use Created, Submitted, Downloading, Paused, Downloaded, Failed, NeedsLinking or Cancelled.";

        filter = Enum.Parse<DownloadJobStatus>(name);
        return null;
    }

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
