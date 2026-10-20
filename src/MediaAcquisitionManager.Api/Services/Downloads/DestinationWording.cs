using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Texts;

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

    /// <summary>
    /// The sentence of <paramref name="destinationKey"/>, naming a folder destination by its name
    /// (<c>{{destination}}</c>), or of <paramref name="categoryKey"/>, naming a category by itself (<c>{{category}}</c>),
    /// with <paramref name="values"/> besides.
    /// </summary>
    /// <remarks>
    /// MAM-47 (ADR 0042) — one whole sentence per shape (design §1.3), never a nested "the destination “X”": every one of
    /// these sentences says "the folder of" that place, and a translation writes the place in that phrase's case (the
    /// Ukrainian genitive) with its own noun.
    /// </remarks>
    public Message Sentence(string destinationKey, string categoryKey, params (string Name, MessageValue Value)[] values) =>
        IsFolder
            ? Message.Of(destinationKey, [("destination", Name), .. values])
            : Message.Of(categoryKey, [("category", Key), .. values]);
}
