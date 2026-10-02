using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-13 — what a request to send a release must name: the request id first (without it a retry couldn't
// be told from a new request), then the release, the download client and the destination. Every refusal is
// pinned with the sentence the Download dialog shows.
public sealed class DownloadRulesTests
{
    // Spelled out rather than read from DownloadRules: they are what the dialog shows.
    private const string RequestIdMissingError =
        "The download has no request id, which keeps a retry from sending it twice. Reload the page and try again.";

    private const string ReleaseMissingError = "Pick the release to send.";
    private const string ClientMissingError = "Pick the download client to send it to.";
    private const string DestinationMissingError = "Pick the destination to send it to.";

    private static readonly Guid Id = Guid.Parse("8b0e6a4c-2d1f-4e3a-9b5c-7d6e5f4a3b21");

    public static TheoryData<string, Guid?, Guid?, Guid?, Guid?, string?> Requests => new()
    {
        { "all four", Id, Id, Id, Id, null },
        { "no request id", null, Id, Id, Id, RequestIdMissingError },
        { "an empty request id", Guid.Empty, Id, Id, Id, RequestIdMissingError },
        { "nothing at all", null, null, null, null, RequestIdMissingError },
        { "no release", Id, null, Id, Id, ReleaseMissingError },
        { "an empty release", Id, Guid.Empty, Id, Id, ReleaseMissingError },
        { "no client", Id, Id, null, Id, ClientMissingError },
        { "an empty client", Id, Id, Guid.Empty, Id, ClientMissingError },
        { "no destination", Id, Id, Id, null, DestinationMissingError },
        { "an empty destination", Id, Id, Id, Guid.Empty, DestinationMissingError },
        { "neither client nor destination", Id, Id, null, null, ClientMissingError },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void Validate_is_the_rule(string label, Guid? requestId, Guid? releaseId, Guid? clientId, Guid? destinationId, string? expected)
    {
        //Arrange

        //Act
        var error = DownloadRules.Validate(requestId, releaseId, clientId, destinationId);

        //Assert
        Assert.True(expected == error, $"{label}: {error}");
    }

    [Theory]
    [InlineData(null, ReleaseMissingError)]
    [InlineData("00000000-0000-0000-0000-000000000000", ReleaseMissingError)]
    [InlineData("8b0e6a4c-2d1f-4e3a-9b5c-7d6e5f4a3b21", null)]
    public void ValidateReleaseId_is_the_rule(string? releaseId, string? expected)
    {
        //Arrange
        Guid? id = releaseId is null ? null : Guid.Parse(releaseId);

        //Act
        var error = DownloadRules.ValidateReleaseId(id);

        //Assert
        Assert.Equal(expected, error);
    }
}
