using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaAcquisitionManager.Api.Tests.Deployment;

// The Dockerfile's HEALTHCHECK is a URL in a text file: rename or move the endpoint it probes and the
// container reports unhealthy forever, with nothing in the build to say why. These tests read the URL
// out of the Dockerfile and send it to the real app.
public sealed partial class ContainerHealthCheckHttpTests
    : IClassFixture<FirstRunApiFactory>, IClassFixture<ThrowingHealthCheckApiFactory>, IAsyncLifetime
{
    private const string ReadyUrl = "/api/health/ready";
    private const string NoHealthCheck = "The Dockerfile has no HEALTHCHECK that probes http://localhost:<port>/<path>.";

    private readonly FirstRunApiFactory _firstRun;
    private readonly ThrowingHealthCheckApiFactory _throwingCheck;

    public ContainerHealthCheckHttpTests(FirstRunApiFactory firstRun, ThrowingHealthCheckApiFactory throwingCheck)
    {
        _firstRun = firstRun;
        _throwingCheck = throwingCheck;
    }

    // Resetting starts the hosts; for FirstRunApiFactory that is the first start of a fresh
    // installation, the case of a new container on an empty volume.
    public async ValueTask InitializeAsync()
    {
        await _firstRun.ResetAsync();
        await _throwingCheck.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Healthcheck_probes_readiness_on_the_port_the_image_listens_on()
    {
        //Arrange
        var dockerfile = await File.ReadAllTextAsync(RepositoryPaths.Dockerfile, TestContext.Current.CancellationToken);

        //Act
        var probe = HealthCheckUrl().Match(dockerfile);
        var listening = HttpPorts().Match(dockerfile);

        //Assert
        Assert.True(probe.Success, NoHealthCheck);
        Assert.True(listening.Success, "The Dockerfile sets no ASPNETCORE_HTTP_PORTS.");
        Assert.Equal(ReadyUrl, probe.Groups["path"].Value);
        Assert.Equal(listening.Groups["port"].Value, probe.Groups["port"].Value);
    }

    [Fact]
    public async Task Healthcheck_url_on_a_fresh_install_is_200_healthy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dockerfile = await File.ReadAllTextAsync(RepositoryPaths.Dockerfile, cancellationToken);
        var probe = HealthCheckUrl().Match(dockerfile);

        //Act
        var response = await _firstRun.GetHttpRequest(probe.Groups["path"].Value);

        //Assert
        Assert.True(probe.Success, NoHealthCheck);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Healthy, body!.Status);
    }

    // curl --fail-with-body exits non-zero on any status of 400 or above, which is what turns the
    // container unhealthy — so a failing check must surface as an error status, not a 200 that says
    // "Unhealthy" in its body.
    [Fact]
    public async Task Healthcheck_url_with_a_failing_readiness_check_is_503()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var dockerfile = await File.ReadAllTextAsync(RepositoryPaths.Dockerfile, cancellationToken);
        var probe = HealthCheckUrl().Match(dockerfile);

        //Act
        var response = await _throwingCheck.GetHttpRequest(probe.Groups["path"].Value);

        //Assert
        Assert.True(probe.Success, NoHealthCheck);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(HealthStatus.Unhealthy, body!.Status);
    }

    // The instruction may continue over several lines ending in a backslash; the URL is on one of them.
    [GeneratedRegex(@"^HEALTHCHECK\b(?:[^\n]*\\\r?\n)*?[^\n]*?http://localhost:(?<port>\d+)(?<path>/[^\s""'|]*)", RegexOptions.Multiline)]
    private static partial Regex HealthCheckUrl();

    [GeneratedRegex(@"\bASPNETCORE_HTTP_PORTS=(?<port>\d+)")]
    private static partial Regex HttpPorts();
}
