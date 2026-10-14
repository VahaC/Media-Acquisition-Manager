using MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations.DownloadClients;

// MAM-30 — the download client contract (DownloadClientContractTests) over qBittorrent's adapter and its fake.
public sealed class QBittorrentDownloadClientContractTests() : DownloadClientContractTests(() => new QBittorrentFakeDownloadClient());
