using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Files;

/// <summary>
/// MAM-42 — which tab of the Files in no library dialog a list request asks for: one readable sentence, or <c>null</c>
/// when the value is acceptable — the rules-class pattern of <c>DownloadJobListRules</c>. Paging is <c>PagingRules</c>',
/// as on every other list.
/// </summary>
public static class UnreportedFileListRules
{
    /// <summary>How much of a value that isn't a view a sentence repeats, as the other list rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// The sentence saying <paramref name="view"/> is not one of the dialog's tabs, or <c>null</c> with the view it names
    /// in <paramref name="filter"/> — <c>null</c> for a blank one, which lists every file the app counts.
    /// </summary>
    /// <param name="view">A view by its name, in any letter case, as a query string carries it (<c>problems</c>);
    /// surrounding whitespace is ignored.</param>
    /// <param name="filter">The view named, when it is one.</param>
    /// <remarks>
    /// By name only: <c>Enum.TryParse</c> would also take <c>1</c> and <c>Problems, Ignored</c>, which it ORs into one
    /// value.
    /// </remarks>
    public static string? ValidateView(string? view, out UnreportedFileView? filter)
    {
        filter = null;
        if (string.IsNullOrWhiteSpace(view))
            return null;

        var text = view.Trim();
        var name = Enum.GetNames<UnreportedFileView>().FirstOrDefault(candidate => candidate.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return $"“{Echo(text)}” is not a view of the files in no library. Use Problems or Ignored, or leave it out for every file.";

        filter = Enum.Parse<UnreportedFileView>(name);
        return null;
    }

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
