using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one <c>torrent-add</c> a <see cref="FakeTransmissionServer"/> read, as its arguments: what the adapter sent,
/// whatever the server answered.
/// </summary>
/// <param name="ArgumentNames">The name of every argument, in the order sent — so a test can assert that only
/// <c>metainfo</c> or <c>filename</c>, <c>download-dir</c>, <c>labels</c> and <c>paused</c> went out.</param>
/// <param name="Kind"><see cref="ReleasePayloadKind.Magnet"/> for a non-empty <c>filename</c>, which Transmission reads
/// before <c>metainfo</c>; <see cref="ReleasePayloadKind.TorrentFile"/> for a non-empty <c>metainfo</c> otherwise;
/// <c>null</c> for neither.</param>
/// <param name="Metainfo">The bytes <c>metainfo</c>'s base64 holds; <c>null</c> when none was sent or it isn't
/// base64.</param>
/// <param name="Filename">The <c>filename</c> as sent — a magnet link from the app; <c>null</c> when none was.</param>
/// <param name="DownloadDir">The <c>download-dir</c> as sent; <c>null</c> when none was.</param>
/// <param name="Labels">The text labels of <c>labels</c> as sent, before Transmission trims them; <c>null</c> when no
/// list was sent.</param>
/// <param name="Paused">The <c>paused</c> flag as sent; <c>null</c> when none was.</param>
public sealed record FakeTransmissionAddRequest(
    IReadOnlyList<string> ArgumentNames,
    ReleasePayloadKind? Kind,
    byte[]? Metainfo,
    string? Filename,
    string? DownloadDir,
    IReadOnlyList<string>? Labels,
    bool? Paused);
