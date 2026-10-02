using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One <c>POST /api/v2/torrents/add</c> a <see cref="FakeQBittorrentServer"/> received with a session it
/// knows (MAM-13), as the form's fields: what the adapter sent, whatever the server answered.
/// </summary>
/// <param name="FieldNames">The name of every field, in the order sent — so a test can assert that no
/// <c>savepath</c> or other field went out.</param>
/// <param name="Kind">What the release was sent as: <see cref="ReleasePayloadKind.TorrentFile"/> for a
/// <c>torrents</c> file part, <see cref="ReleasePayloadKind.Magnet"/> for <c>urls</c>; <c>null</c> for
/// neither.</param>
/// <param name="Category">The <c>category</c> field exactly as sent; <c>null</c> when none was.</param>
/// <param name="AutoTmm">The <c>autoTMM</c> field as sent; <c>null</c> when none was.</param>
/// <param name="Urls">The <c>urls</c> field as sent; <c>null</c> when none was.</param>
/// <param name="TorrentFile">The bytes of the <c>torrents</c> file part; <c>null</c> when none was sent.</param>
/// <param name="TorrentFileName">That part's file name.</param>
/// <param name="TorrentContentType">That part's <c>Content-Type</c>.</param>
public sealed record FakeQBittorrentAddRequest(
    IReadOnlyList<string> FieldNames,
    ReleasePayloadKind? Kind,
    string? Category,
    string? AutoTmm,
    string? Urls,
    byte[]? TorrentFile,
    string? TorrentFileName,
    string? TorrentContentType);
