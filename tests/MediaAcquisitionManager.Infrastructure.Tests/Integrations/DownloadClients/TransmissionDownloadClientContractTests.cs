using MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations.DownloadClients;

// MAM-30 — the download client contract (DownloadClientContractTests) over Transmission's adapter and its fake.
public sealed class TransmissionDownloadClientContractTests() : DownloadClientContractTests(() => new TransmissionFakeDownloadClient());
