using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Abstractions;

public sealed class ReleasePayloadRecordsTests
{
    private const string ApiKey = "planted-release-key";
    private const string Passkey = "planted-tracker-passkey";

    [Fact]
    public void ReleasePayloadLocator_ToString_masks_the_value()
    {
        //Arrange
        var locator = new ReleasePayloadLocator($"http://127.0.0.1:9/1/download?apikey={ApiKey}&link=x");

        //Act
        var text = locator.ToString();
        var interpolated = $"Fetching {locator}";

        //Assert
        Assert.Equal("ReleasePayloadLocator { *** }", text);
        Assert.DoesNotContain(ApiKey, interpolated);
    }

    [Fact]
    public void ReleasePayload_ToString_leaves_out_the_magnet_and_the_file()
    {
        //Arrange
        var magnet = new ReleasePayload(ReleasePayloadKind.Magnet, null, $"magnet:?xt=urn:btih:abc&tr=http://tracker.test/{Passkey}/announce");
        var torrentFile = new ReleasePayload(ReleasePayloadKind.TorrentFile, [1, 2, 3], null);

        //Act
        var magnetText = magnet.ToString();
        var torrentFileText = torrentFile.ToString();

        //Assert
        Assert.Equal("ReleasePayload { Kind = Magnet }", magnetText);
        Assert.Equal("ReleasePayload { Kind = TorrentFile }", torrentFileText);
        Assert.DoesNotContain(Passkey, magnetText);
    }

    [Fact]
    public void A_search_hit_prints_its_locator_masked()
    {
        //Arrange
        var hit = new ReleaseSearchHit(
            "https://tracker.test/t1", "Interstellar (2014)", 1, 2, 3, null, null, [], ReleasePayloadKind.TorrentFile, null, [],
            new ReleasePayloadLocator($"http://127.0.0.1:9/1/download?apikey={ApiKey}"),
            ReleaseKind.Movie,
            null);

        //Act
        var text = hit.ToString();

        //Assert
        Assert.DoesNotContain(ApiKey, text);
        Assert.Contains("ReleasePayloadLocator { *** }", text);
    }
}
