using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Tests.Contracts;

// MAM-7 — MVC logs a controller action's arguments at Trace through their ToString, so each request
// that carries a secret must print it masked (ADR 0007).
public sealed class SecretValueUpsertTests
{
    private const string Password = "planted-password-1";
    private const string ApiKey = "planted-api-key-0123456789";

    [Fact]
    public void ToString_masks_a_set_value_and_says_null_for_none()
    {
        //Arrange
        var set = new SecretValueUpsert(SecretUpsertMode.Set, ApiKey);
        var clear = new SecretValueUpsert(SecretUpsertMode.Clear);

        //Act
        var setText = set.ToString();
        var clearText = clear.ToString();

        //Assert
        Assert.Equal("SecretValueUpsert { Mode = Set, Value = *** }", setText);
        Assert.Equal("SecretValueUpsert { Mode = Clear, Value = null }", clearText);
    }

    [Fact]
    public void ToString_of_every_request_that_carries_a_secret_prints_neither_value()
    {
        //Arrange
        var password = new SecretValueUpsert(SecretUpsertMode.Set, Password);
        var apiKey = new SecretValueUpsert(SecretUpsertMode.Set, ApiKey);
        var create = new CreateIntegrationRequest("qbittorrent", "Downloads", "http://127.0.0.1:9", "admin", password, apiKey);
        var update = new UpdateIntegrationRequest("Downloads", "http://127.0.0.1:9", "admin", password, apiKey);
        var test = new TestIntegrationRequest(Guid.NewGuid(), "qbittorrent", "http://127.0.0.1:9", "admin", password, apiKey);

        //Act
        string[] texts = [create.ToString(), update.ToString(), test.ToString()];

        //Assert
        Assert.All(texts, text =>
        {
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, text, StringComparison.Ordinal);
            Assert.Contains("Password = SecretValueUpsert { Mode = Set, Value = *** }", text, StringComparison.Ordinal);
            Assert.Contains("ApiKey = SecretValueUpsert { Mode = Set, Value = *** }", text, StringComparison.Ordinal);
        });
    }
}
