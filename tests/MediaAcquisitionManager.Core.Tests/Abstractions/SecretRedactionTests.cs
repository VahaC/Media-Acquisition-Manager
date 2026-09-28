using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Core.Tests.Abstractions;

public sealed class SecretRedactionTests
{
    private const string ApiKey = "k3y-0123456789abcdef";
    private const string Password = "correct-horse-battery";

    [Fact]
    public void Redact_replaces_every_occurrence_of_every_secret()
    {
        //Arrange
        var text = $"401 for key {ApiKey} (user:{Password}); retried with {ApiKey}.";

        //Act
        var redacted = SecretRedaction.Redact(text, ApiKey, Password);

        //Assert
        Assert.Equal("401 for key *** (user:***); retried with ***.", redacted);
    }

    [Fact]
    public void Redact_is_case_sensitive_so_similar_text_is_left_alone()
    {
        //Arrange
        var text = $"Rejected {ApiKey.ToUpperInvariant()}";

        //Act
        var redacted = SecretRedaction.Redact(text, ApiKey);

        //Assert
        Assert.Equal(text, redacted);
    }

    [Fact]
    public void Redact_skips_secrets_shorter_than_eight_characters()
    {
        //Arrange
        const string shortSecret = "abc1234";
        var text = $"Token {shortSecret} rejected";

        //Act
        var redacted = SecretRedaction.Redact(text, shortSecret);

        //Assert
        Assert.Equal(text, redacted);
    }

    [Fact]
    public void Redact_masks_a_secret_of_exactly_eight_characters()
    {
        //Arrange
        const string secret = "abcd1234";

        //Act
        var redacted = SecretRedaction.Redact($"Token {secret} rejected", secret);

        //Assert
        Assert.Equal("Token *** rejected", redacted);
    }

    [Fact]
    public void Redact_ignores_null_and_empty_secrets()
    {
        //Arrange
        var text = $"Rejected {ApiKey}";

        //Act
        var redacted = SecretRedaction.Redact(text, null, "", ApiKey);

        //Assert
        Assert.Equal("Rejected ***", redacted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_of_null_or_empty_text_returns_it_unchanged(string? text)
    {
        //Arrange
        var secrets = new[] { ApiKey };

        //Act
        var redacted = SecretRedaction.Redact(text, secrets);

        //Assert
        Assert.Equal(text, redacted);
    }

    [Fact]
    public void Redact_without_secrets_returns_the_text_unchanged()
    {
        //Arrange
        const string text = "Upstream said no.";

        //Act
        var redacted = SecretRedaction.Redact(text);

        //Assert
        Assert.Equal(text, redacted);
    }
}
