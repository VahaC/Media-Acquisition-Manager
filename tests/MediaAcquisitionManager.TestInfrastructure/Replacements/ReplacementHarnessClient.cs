namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-30 — the download client a <see cref="ReplacementVerificationHarness"/> sends its replacements to, each behind its
/// fake and the real adapter: qBittorrent, whose destinations are categories, or Transmission, whose destinations are
/// folders (ADR 0039).
/// </summary>
public enum ReplacementHarnessClient
{
    /// <summary>qBittorrent, with the categories Movies and TV Shows: what every harness stood on before MAM-30.</summary>
    QBittorrent = 1,

    /// <summary>Transmission, with the destinations Movies and TV Shows saving into its <c>/downloads/movies</c> and
    /// <c>/downloads/tv-shows</c>.</summary>
    Transmission = 2,
}
