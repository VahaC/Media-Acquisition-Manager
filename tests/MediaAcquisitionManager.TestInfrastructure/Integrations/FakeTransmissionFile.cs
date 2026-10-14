namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one file of a <see cref="FakeTransmissionTorrent"/>: what <c>torrent-get</c> lists for it in <c>files</c>
/// and, at the same index, in <c>fileStats</c>.
/// </summary>
/// <param name="Name">Its path below the torrent's <c>downloadDir</c>, <c>/</c>-separated and cleaned as Transmission
/// cleans names: the torrent's folder first for a torrent of several files, the file alone for a torrent of one. Never
/// with the <c>.part</c> an unfinished file carries on disk.</param>
/// <param name="Length">Its size in bytes.</param>
/// <param name="BytesCompleted">How many of its bytes Transmission has.</param>
/// <param name="Wanted">Whether it is to be downloaded (<c>fileStats[].wanted</c>).</param>
/// <param name="Priority"><c>fileStats[].priority</c>: -1 low, 0 normal, 1 high.</param>
public sealed record FakeTransmissionFile(string Name, long Length, long BytesCompleted, bool Wanted = true, int Priority = 0);
