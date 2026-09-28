using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.TestInfrastructure;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Http.Probes;

namespace MediaAcquisitionManager.Api.Tests.Http;

public sealed partial class SecurityHeadersHttpTests : IClassFixture<PipelineProbeApiFactory>, IAsyncLifetime
{
    private const string ApiUrl = "/api/health";
    private const string SpaUrl = "/library";

    // The policy the design specifies, spelled out rather than read from
    // SecurityHeadersMiddleware.ContentSecurityPolicy: comparing the header with the constant that
    // produced it passes whatever the constant says. {0} is the theme script's hash, recomputed from
    // frontend/index.html.
    private const string ExpectedContentSecurityPolicy =
        "default-src 'self'; script-src 'self' '{0}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    private readonly PipelineProbeApiFactory _factory;

    public SecurityHeadersHttpTests(PipelineProbeApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(ApiUrl)]
    [InlineData(SpaUrl)]
    public async Task Response_carries_every_security_header(string url)
    {
        //Arrange
        var themeScriptHash = await ThemeScriptHashFromIndexHtmlAsync(TestContext.Current.CancellationToken);
        var expected = new Dictionary<string, string>
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["Referrer-Policy"] = "no-referrer",
            ["Content-Security-Policy"] = string.Format(CultureInfo.InvariantCulture, ExpectedContentSecurityPolicy, themeScriptHash),
        };

        //Act
        var response = await _factory.GetHttpRequest(url);

        //Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.All(expected, header => Assert.Equal(header.Value, Assert.Single(response.Headers.GetValues(header.Key))));
    }

    [Fact]
    public async Task Csp_script_src_does_not_allow_inline_script()
    {
        //Arrange

        //Act
        var response = await _factory.GetHttpRequest(SpaUrl);

        //Assert
        var scriptSrc = ScriptSrcOf(response);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csp_pins_the_hash_of_the_inline_theme_script_in_frontend_index_html()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var html = await File.ReadAllTextAsync(RepositoryPaths.FrontendIndexHtml, cancellationToken);
        var inlineScripts = InlineScript().Matches(html);
        var fileScriptHash = await ThemeScriptHashFromIndexHtmlAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(SpaUrl);

        //Assert
        // On failure: the inline script in frontend/index.html changed. Put the "Actual" hash into
        // SecurityHeadersMiddleware.ThemeScriptHash, or the browser will refuse to run the script.
        Assert.Single(inlineScripts);
        Assert.Equal(SecurityHeadersMiddleware.ThemeScriptHash, fileScriptHash);
        Assert.Contains($"'{fileScriptHash}'", ScriptSrcOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Header_an_endpoint_set_itself_is_kept()
    {
        //Arrange
        const string url = PipelineProbeController.BaseUrl + "/referrer-policy";

        //Act
        var response = await _factory.GetHttpRequest(url);

        //Assert
        Assert.Equal(PipelineProbeController.OwnReferrerPolicy, Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    // The hash is over the exact UTF-8 text between the tags of the first inline script — what the
    // browser hashes.
    private static async Task<string> ThemeScriptHashFromIndexHtmlAsync(CancellationToken cancellationToken)
    {
        var html = await File.ReadAllTextAsync(RepositoryPaths.FrontendIndexHtml, cancellationToken);
        var script = InlineScript().Match(html).Groups["body"].Value;
        return "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
    }

    private static string ScriptSrcOf(HttpResponseMessage response) =>
        Assert.Single(
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")).Split("; "),
            directive => directive.StartsWith("script-src ", StringComparison.Ordinal));

    // A <script> tag without attributes: the inline theme script. The module entry point carries
    // type= and src= attributes, so it can't match.
    [GeneratedRegex("<script>(?<body>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex InlineScript();
}
