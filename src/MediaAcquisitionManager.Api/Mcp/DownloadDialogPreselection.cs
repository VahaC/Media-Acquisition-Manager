using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Mcp;

/// <summary>
/// MAM-45 — the client and destination the web app's Download dialog opens on, for <c>get_download_options</c> (Vasyl's
/// decision d): the C# twin of <c>preselect()</c> and <c>destinationOf()</c> in
/// <c>frontend/src/components/search/DownloadDialog.tsx</c>, so an assistant offers the user what the dialog would.
/// </summary>
/// <remarks>
/// Only the opening pick: the dialog's <c>destinationPickedWith()</c> — the default destination once the person picks a
/// client by hand — has no twin, as an assistant picks no client; it names the ids the user chose instead. A change to
/// either function in the dialog belongs here too, or the two would offer different things for the same release.
/// </remarks>
public static class DownloadDialogPreselection
{
    /// <summary>
    /// The pick the dialog opens with: the client holding the suggested destination (MAM-32 — the server's pick, by the
    /// release's tracker section or else the default), else the only client; and that client's destination by
    /// <see cref="DestinationOf"/>. Neither when there are several clients and no suggestion, as the dialog then shows
    /// its placeholders.
    /// </summary>
    public static (DownloadClientOptionDto? Client, DownloadDestinationOptionDto? Destination) Of(DownloadOptionsResponse options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var suggestedId = options.SuggestedDestinationId;
        var client = options.Clients.FirstOrDefault(candidate => candidate.Destinations.Any(destination => destination.Id == suggestedId))
            ?? (options.Clients.Count == 1 ? options.Clients[0] : null);
        return (client, DestinationOf(client, suggestedId));
    }

    /// <summary>
    /// A client's own destination to open on: the suggested one when it holds it, else its only one, else none. Never the
    /// default by itself: none suggested means the server weighed it and picked nothing.
    /// </summary>
    private static DownloadDestinationOptionDto? DestinationOf(DownloadClientOptionDto? client, Guid? suggestedId)
    {
        var destinations = client?.Destinations ?? [];
        return destinations.FirstOrDefault(destination => destination.Id == suggestedId)
            ?? (destinations.Count == 1 ? destinations[0] : null);
    }
}
