using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — one new video file of a replacement's download, as its client listed it (check 1, ADR 0035).</summary>
/// <param name="Position">Its 0-based place among every file the client listed for the download, in the client's own
/// order.</param>
/// <param name="File">What the client said of it.</param>
public sealed record ClientNewFile(int Position, ClientJobFile File);
