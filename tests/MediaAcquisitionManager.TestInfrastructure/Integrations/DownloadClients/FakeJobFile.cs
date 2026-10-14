namespace MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

/// <summary>MAM-30 — one file of a job an <see cref="IFakeDownloadClient"/> plants, in no client's words.</summary>
/// <param name="RelativePath">Its path below the destination's folder, <c>/</c>-separated, the torrent's own folder first
/// when it has one — as both clients name it.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="BytesDone">How many of its bytes the client has.</param>
/// <param name="Wanted">Whether the client downloads it.</param>
public sealed record FakeJobFile(string RelativePath, long Size, long BytesDone, bool Wanted = true);
