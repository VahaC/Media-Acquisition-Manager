using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, D5 — the overwrite check itself never lets a send go to a folder its client can't see, Keep both or a
// replacement alike, whatever the release is: the client would create that folder in its own filesystem. The send refuses
// such a folder before it asks the check (DownloadsSendFolderClientHttpTests), so no request reaches this branch; the
// check is asked here the way the send asks it, with the destination as the client listed it, so its public contract
// holds without that ordering.
public sealed class DownloadOverwriteGuardFolderNotSeenTests : IClassFixture<FolderClientApiFactory>, IAsyncLifetime
{
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string MagnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Film.2001";
    private const string UnusableError =
        "“Transmission” can't use the folder of the destination “Фільми” any more, so nothing was sent. The owner checks the destination under Destinations in the user menu.";

    private readonly FolderClientApiFactory _factory;
    private Guid _clientId;

    public DownloadOverwriteGuardFolderNotSeenTests(FolderClientApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibraryLocationSeeder(_factory.Scope).Create(jellyfin, "Фільми", "/media/movies");
        // Transmission's /downloads is the app's /media: the destination Фільми is the library's folder, which its
        // mappings place inside the Media folders.
        _clientId = (await integrations.Create(
            FakeFolderDownloadClient.TypeId, "Transmission", pathMappings: [new PathMappingPair("/downloads", "/media")])).Id;
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        _factory.FolderClient.Arm();
        // A different file where the release would land: a check that looked would refuse with another sentence.
        _factory.Files.AddFile("/media/movies/Film.2001.mkv", TestMedia.File(inode: 1, sizeBytes: 1));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(ReleasePayloadKind.TorrentFile, false)]
    [InlineData(ReleasePayloadKind.TorrentFile, true)]
    [InlineData(ReleasePayloadKind.Magnet, false)]
    [InlineData(ReleasePayloadKind.Magnet, true)]
    public async Task CheckAsync_of_a_folder_the_client_can_not_see_refuses_it_in_either_mode_and_looks_at_nothing(
        ReleasePayloadKind kind, bool anyFileRefuses)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.FolderClient.CantSee(MoviesFolder);
        var payload = kind == ReleasePayloadKind.Magnet
            ? new ReleasePayload(kind, null, MagnetUri)
            : new ReleasePayload(kind, TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 5, seed: 1)), null);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var client = await db.IntegrationConnections.Include(connection => connection.PathMappings)
            .SingleAsync(connection => connection.Id == _clientId, cancellationToken);
        var listed = await scope.ServiceProvider.GetRequiredService<IDestinationChecker>().ListAsync(client, [MoviesFolder], cancellationToken);
        var destination = listed.Find(MoviesFolder)!;
        var settings = new IntegrationConnectionSettings(client.Id, "http://transmission.test:9091", null, null, null);
        var guard = scope.ServiceProvider.GetRequiredService<IDownloadOverwriteGuard>();

        //Act
        var verdict = await guard.CheckAsync(
            payload, destination, "Фільми", client, _factory.FolderClient, settings, anyFileRefuses, cancellationToken);

        //Assert
        Assert.Equal(DestinationPathStatus.FolderNotSeen, destination.Check.Status);
        // Never "sent unchecked": no check value a sent job could carry, and the send's own sentence naming the
        // destination, never its folder (D13).
        Assert.Null(verdict.Check);
        Assert.Equal(UnusableError, TestTexts.En(verdict.Refusal));
        // Nothing looked into, and nothing asked of the client beyond the listing that found the folder unseen.
        Assert.Empty(_factory.Files.DescribedPaths);
        Assert.Equal(["ListDestinations"], _factory.FolderClient.Calls);
    }
}
