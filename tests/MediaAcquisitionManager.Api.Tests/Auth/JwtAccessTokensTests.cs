using System.Security.Cryptography;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Core.Options;

namespace MediaAcquisitionManager.Api.Tests.Auth;

// Not an endpoint: the key is built when the first token is signed or checked, and a bad one fails
// that request with a 500 that quotes only a trace id. What matters is the sentence the operator
// then reads in the log.
public sealed class JwtAccessTokensTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    public void CreateSigningKey_with_a_secret_shorter_than_32_bytes_refuses_and_names_the_setting(int length)
    {
        //Arrange
        var options = new JwtOptions { Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(length)) };

        //Act
        var exception = Record.Exception(() => JwtAccessTokens.CreateSigningKey(options));

        //Assert
        var refusal = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            "Jwt:Secret is shorter than 32 bytes, too short to sign HS256 tokens. Set MAM_Jwt__Secret to base64 " +
            "of 64 random bytes, or leave it unset to let the app generate one.",
            refusal.Message);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    public void CreateSigningKey_with_a_secret_of_32_bytes_or_more_is_that_key(int length)
    {
        //Arrange
        var secret = RandomNumberGenerator.GetBytes(length);
        var options = new JwtOptions { Secret = Convert.ToBase64String(secret) };

        //Act
        var key = JwtAccessTokens.CreateSigningKey(options);

        //Assert
        // Guards the guard: the boundary is 32 bytes, not something stricter than HS256 needs.
        Assert.Equal(secret, key.Key);
    }
}
