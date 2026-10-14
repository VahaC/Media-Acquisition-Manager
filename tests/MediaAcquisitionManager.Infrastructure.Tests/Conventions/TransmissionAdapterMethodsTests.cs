using System.Reflection;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Conventions;

// MAM-30 — Transmission's RPC has a method for every write the app must never send: stopping, removing, moving or renaming
// a torrent or its files, or changing Transmission's own settings (ADR 0014, ADR 0031). DownloadClientNeverRemovesTests keeps
// the contract's member names clean, but a Transmission write is a method name in a request body, which no member name shows.
// So the adapter names its methods in constants, and this convention holds them to the four it may send; the last test
// proves it by behaviour, calling every member against a Transmission that has torrents.
public sealed class TransmissionAdapterMethodsTests
{
    /// <summary>The methods the adapter may send: three reads and the one write.</summary>
    private static readonly string[] AllowedMethods = ["session-get", "free-space", "torrent-get", "torrent-add"];

    /// <summary>Every method of Transmission's legacy RPC that changes a torrent or Transmission (rpc-spec.md, 4.0 and 4.1).</summary>
    private static readonly string[] ForbiddenMethods =
    [
        "torrent-start", "torrent-start-now", "torrent-stop", "torrent-verify", "torrent-reannounce", "torrent-set",
        "torrent-remove", "torrent-set-location", "torrent-rename-path", "session-set", "session-close", "blocklist-update",
        "port-test", "queue-move-top", "queue-move-up", "queue-move-down", "queue-move-bottom", "group-set",
    ];

    /// <summary>Forbidden methods, for the detector's own test.</summary>
    public static TheoryData<string> Forbidden => [.. ForbiddenMethods];

    [Fact]
    public void The_adapters_rpc_methods_are_session_get_free_space_torrent_get_and_torrent_add_and_nothing_else()
    {
        //Arrange

        //Act
        var methods = MethodConstants().Values.Order(StringComparer.Ordinal).ToList();

        //Assert
        Assert.Equal(AllowedMethods.Order(StringComparer.Ordinal), methods);
    }

    [Fact]
    public void No_text_constant_of_the_adapter_names_a_method_that_changes_a_torrent_or_transmission()
    {
        //Arrange
        var constants = TextConstants();

        //Act
        var forbidden = constants.Where(constant => NamesAForbiddenMethod(constant.Value)).Select(constant => constant.Key).ToList();

        //Assert
        Assert.NotEmpty(constants);
        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void The_check_finds_each_forbidden_method_as_a_constant_would_hold_it(string method)
    {
        //Arrange

        //Act
        var found = NamesAForbiddenMethod(method);

        //Assert
        Assert.True(found);
    }

    [Fact]
    public async Task Every_member_sends_only_the_four_methods_and_writes_nothing_but_one_add()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var transmission = new FakeTransmissionServer();
        transmission.AddFolder("/downloads/movies");
        transmission.AddTorrent(TransmissionSamples.BigBuckBunnySeeding());
        transmission.AddTorrent(TransmissionSamples.MagnetWithoutMetadata());
        transmission.Arm(harness.TransmissionHttp);
        var settings = new IntegrationConnectionSettings(
            Guid.NewGuid(), transmission.BaseUrl, FakeTransmissionServer.DefaultUserName, FakeTransmissionServer.DefaultPassword, null);
        var client = harness.Adapter<IDownloadClient>(TransmissionAdapter.TypeId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var torrent = TestTorrent.V1("Interstellar (2014).mkv", TestTorrent.Bytes(40_000, 7));

        //Act
        await harness.Adapter(TransmissionAdapter.TypeId).TestConnectionAsync(settings, cancellationToken);
        await client.ListDestinationsAsync(settings, ["/downloads/movies"], cancellationToken);
        await client.SubmitAsync(
            settings, new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.TorrentFile, torrent, null), "/downloads/movies", null, "Movies"), cancellationToken);
        await client.FindJobsAsync(settings, [TransmissionSamples.BigBuckBunnyHash, TransmissionSamples.MagnetHash], cancellationToken);
        await client.ListJobFilesAsync(settings, TransmissionSamples.BigBuckBunnyHash, cancellationToken);
        await client.ListDestinationJobsAsync(settings, "/downloads/movies", cancellationToken);
        await client.ListJobsWithFilesAsync(settings, cancellationToken);

        //Assert
        Assert.Equal(AllowedMethods.Order(StringComparer.Ordinal), transmission.Calls.Select(call => call.Method).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(["torrent-add"], transmission.Writes.Select(call => call.Method));
    }

    /// <summary>The adapter's constants named <c>…Method</c>, by name: where its RPC methods live.</summary>
    private static Dictionary<string, string> MethodConstants() =>
        TextConstants().Where(constant => constant.Key.EndsWith("Method", StringComparison.Ordinal)).ToDictionary();

    /// <summary>Every text constant the adapter declares, whatever its visibility, by name.</summary>
    private static Dictionary<string, string> TextConstants() =>
        typeof(TransmissionAdapter)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

    /// <summary>Whether <paramref name="text"/> is a forbidden method, exactly as a request would name it.</summary>
    private static bool NamesAForbiddenMethod(string text) => ForbiddenMethods.Contains(text.Trim(), StringComparer.Ordinal);
}
