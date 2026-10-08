namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>One job of a download client, as the client reports it.</summary>
/// <param name="ClientJobId">The client's id of the job.</param>
/// <param name="Name">Its name.</param>
/// <param name="State">Its state.</param>
/// <param name="Progress">From 0 to 1; <c>null</c> when unknown.</param>
/// <param name="DestinationKey">The client destination it is in; <c>null</c> when none.</param>
/// <param name="ServiceContentPath">Where its content is, as the client sees it.</param>
/// <param name="InfoHashes">
/// Every info hash the client lists for the job, lower-case hexadecimal (MAM-13): a job is matched to a
/// submission by these, never by its name (specification §3.2, step 6). Empty for a job that isn't a
/// torrent.
/// </param>
/// <param name="ServiceSavePath">
/// MAM-38 — the folder its files end up in, as the client sees it (qBittorrent's <c>save_path</c>), which its
/// files' relative paths are below; <c>null</c> when the client doesn't say. Unlike
/// <paramref name="ServiceContentPath"/>, never the folder an unfinished download is kept in meanwhile.
/// </param>
public sealed record ClientJob(
    string ClientJobId,
    string Name,
    ClientJobState State,
    double? Progress,
    string? DestinationKey,
    string? ServiceContentPath,
    IReadOnlyList<string> InfoHashes,
    string? ServiceSavePath = null);
