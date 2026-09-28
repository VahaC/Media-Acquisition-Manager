using System.Net;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Api.Controllers.Settings.Probes;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

// MAM-4 — pins SettingsWritesAreOwnerOnlyConvention itself, through SettingsProbeController: a
// settings controller with nothing but the convention guarding it. The app's own UsersController is
// owner-only as a whole, so its attribute answers first, and SettingsWritesAreOwnerOnlyHttpTests
// would stay green with the convention removed or inverted.
public sealed class SettingsWritesAreOwnerOnlyConventionHttpTests : IClassFixture<PipelineProbeApiFactory>, IAsyncLifetime
{
    private const string OwnerOnlyBody = """{"error":"Only the installation owner can do this."}""";

    private readonly PipelineProbeApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public SettingsWritesAreOwnerOnlyConventionHttpTests(PipelineProbeApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Each write the probe answers: the method-bound ones, and the action with no method attribute
    // sent as two of the writes it answers too.
    [Theory]
    [InlineData("POST", SettingsProbeController.BaseUrl)]
    [InlineData("PUT", SettingsProbeController.BaseUrl)]
    [InlineData("DELETE", SettingsProbeController.BaseUrl)]
    [InlineData("POST", SettingsProbeController.AnyMethodUrl)]
    [InlineData("DELETE", SettingsProbeController.AnyMethodUrl)]
    public async Task Settings_write_as_a_signed_in_non_owner_is_403_with_the_owner_sentence(string method, string url)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await SendAsync(method, url, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OwnerOnlyBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("POST", SettingsProbeController.BaseUrl)]
    [InlineData("PUT", SettingsProbeController.BaseUrl)]
    [InlineData("DELETE", SettingsProbeController.BaseUrl)]
    [InlineData("POST", SettingsProbeController.AnyMethodUrl)]
    [InlineData("DELETE", SettingsProbeController.AnyMethodUrl)]
    public async Task Settings_write_as_the_owner_is_2xx(string method, string url)
    {
        //Arrange

        //Act
        var response = await SendAsync(method, url, _owner.AccessToken);

        //Assert
        // Guards the guard: the 403s above come from the owner check, not from a probe that refuses
        // everyone.
        Assert.True(response.IsSuccessStatusCode, $"{method} {url} answered {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Settings_read_as_a_signed_in_non_owner_is_200()
    {
        //Arrange

        //Act
        var response = await SendAsync("GET", SettingsProbeController.BaseUrl, _member.AccessToken);

        //Assert
        // Reads are the controller's call: the convention gates writes only.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(string method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return _factory.SendHttpRequest(request);
    }
}
