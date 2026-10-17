using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Abstractions;

public sealed class IntegrationConnectionSettingsTests
{
    private const string Password = "planted-password-1";
    private const string ApiKey = "planted-api-key-0123456789";

    [Fact]
    public void ToString_masks_the_password_and_the_api_key()
    {
        //Arrange
        var connectionId = Guid.Parse("3f2504e0-4f89-41d3-9a0c-0305e82c3301");
        var settings = new IntegrationConnectionSettings(connectionId, "http://127.0.0.1:9", "admin", Password, ApiKey);

        //Act
        var text = settings.ToString();

        //Assert
        Assert.Equal(
            "IntegrationConnectionSettings { ConnectionId = 3f2504e0-4f89-41d3-9a0c-0305e82c3301, BaseUrl = http://127.0.0.1:9, " +
            "UserName = admin, Password = ***, ApiKey = ***, CatalogLanguage =  }",
            text);
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain(ApiKey, text);
    }

    [Fact]
    public void ToString_is_masked_inside_an_interpolated_string_too()
    {
        //Arrange
        var settings = new IntegrationConnectionSettings(null, "http://127.0.0.1:9", null, Password, ApiKey);

        //Act
        var text = $"Testing {settings}";

        //Assert
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain(ApiKey, text);
    }

    [Fact]
    public void ToString_says_null_for_a_missing_secret()
    {
        //Arrange
        var settings = new IntegrationConnectionSettings(null, "http://127.0.0.1:9", null, null, null);

        //Act
        var text = settings.ToString();

        //Assert
        Assert.Equal(
            "IntegrationConnectionSettings { ConnectionId = , BaseUrl = http://127.0.0.1:9, UserName = , Password = null, ApiKey = null, CatalogLanguage =  }",
            text);
    }

    [Fact]
    public void ToString_shows_the_catalog_language_which_is_no_secret()
    {
        //Arrange
        var settings = new IntegrationConnectionSettings(null, "http://127.0.0.1:9", null, null, ApiKey, "uk-UA");

        //Act
        var text = settings.ToString();

        //Assert
        Assert.Equal(
            "IntegrationConnectionSettings { ConnectionId = , BaseUrl = http://127.0.0.1:9, UserName = , Password = null, ApiKey = ***, CatalogLanguage = uk-UA }",
            text);
        Assert.DoesNotContain(ApiKey, text);
    }

    [Fact]
    public void Secrets_lists_the_password_and_the_api_key()
    {
        //Arrange
        var settings = new IntegrationConnectionSettings(null, "http://127.0.0.1:9", "admin", Password, ApiKey);

        //Act
        var secrets = settings.Secrets;

        //Assert
        Assert.Equal(new[] { Password, ApiKey }, secrets);
        Assert.DoesNotContain("admin", secrets);
    }

    [Theory]
    [InlineData("http://h:8096", "System/Info", "http://h:8096/System/Info")]
    [InlineData("http://h/jellyfin", "/System/Info", "http://h/jellyfin/System/Info")]
    [InlineData("http://192.168.1.195:9696/1", "api?t=caps&apikey=abc", "http://192.168.1.195:9696/1/api?t=caps&apikey=abc")]
    [InlineData("http://h:8080", "api/v2/app/version?x=1", "http://h:8080/api/v2/app/version?x=1")]
    public void Resolve_joins_the_base_url_and_a_relative_path(string baseUrl, string relativePath, string expected)
    {
        //Arrange
        var settings = new IntegrationConnectionSettings(null, baseUrl, null, null, null);

        //Act
        var uri = settings.Resolve(relativePath);

        //Assert
        Assert.Equal(expected, uri.AbsoluteUri);
    }
}
