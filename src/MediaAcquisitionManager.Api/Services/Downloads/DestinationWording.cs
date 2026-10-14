using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>
/// MAM-30 — how the app's own sentences about a send name where it goes: a category by its name, as qBittorrent's
/// always have, and a folder destination by the destination's name, never the folder — members read these sentences
/// on their jobs and never see a folder (ADR 0039, D13).
/// </summary>
/// <param name="Kind">What the client's destinations are.</param>
/// <param name="Key">The destination's stored key: the category, or the folder.</param>
/// <param name="Name">The destination's name.</param>
internal sealed record DestinationWording(DownloadDestinationKind Kind, string Key, string Name)
{
    /// <summary>Whether the destination is a folder.</summary>
    public bool IsFolder => Kind == DownloadDestinationKind.Folder;

    /// <summary>"the category “Movies”", or "the destination “Фільми”".</summary>
    public string Destination => IsFolder ? $"the destination “{Name}”" : $"the category “{Key}”";
}
