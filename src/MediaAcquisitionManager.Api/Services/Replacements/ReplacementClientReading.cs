using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — how one download client's whole listing was read for the held marks (ADR 0031); anything but
/// <see cref="ReplacementClientCheck.Read"/> is uncertain, never an all-clear.</summary>
/// <param name="ClientName">The client.</param>
/// <param name="Status">Read, read in part, or not read.</param>
/// <param name="Error">Why it wasn't read, every secret of the call masked, as a message the Api renders (MAM-47);
/// <c>null</c> unless <see cref="ReplacementClientCheck.NotRead"/>.</param>
/// <param name="UnplacedTorrents">How many of its torrents' files couldn't be placed in the app's folders.</param>
/// <param name="OutsideTorrents">MAM-28 — how many of its torrents list a file its mappings put outside the allowed folders
/// that may be a name the move would rename — of that name's file name in any letter case, of no size, or of its size
/// unless its file has several names —: never looked at there, so never ruled out. Counted only right before a move, for a
/// client the app sent a replacement to; 0 otherwise.</param>
public sealed record ReplacementClientReading(
    string ClientName, ReplacementClientCheck Status, Message? Error, int UnplacedTorrents, int OutsideTorrents = 0);
