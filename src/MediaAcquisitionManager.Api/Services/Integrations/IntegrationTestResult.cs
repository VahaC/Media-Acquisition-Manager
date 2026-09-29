using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Integrations;

/// <summary>The outcome of a request to test a connection.</summary>
/// <param name="Status">How the request ended.</param>
/// <param name="Response">What the service answered, when <see cref="IntegrationTestStatus.Tested"/>.</param>
/// <param name="Error">The sentence for the owner when the test couldn't run and there is a reason to
/// give.</param>
public sealed record IntegrationTestResult(IntegrationTestStatus Status, ConnectionTestResponse? Response, string? Error)
{
    public static IntegrationTestResult NotFound { get; } = new(IntegrationTestStatus.NotFound, null, null);

    public static IntegrationTestResult Tested(ConnectionTestResponse response) => new(IntegrationTestStatus.Tested, response, null);

    public static IntegrationTestResult EditedIntegrationMissing(string error) =>
        new(IntegrationTestStatus.EditedIntegrationMissing, null, error);

    public static IntegrationTestResult Invalid(string error) => new(IntegrationTestStatus.Invalid, null, error);

    public static IntegrationTestResult AdapterUnavailable(string error) => new(IntegrationTestStatus.AdapterUnavailable, null, error);

    public static IntegrationTestResult SecretUnreadable(string error) => new(IntegrationTestStatus.SecretUnreadable, null, error);
}
