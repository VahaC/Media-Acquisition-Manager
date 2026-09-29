using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Security;

public sealed class AesGcmEncryptionServiceTests
{
    private const string TestKey = "3q2+7wABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhs=";
    private const string OtherKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
    private const string Secret = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    private static readonly Guid IntegrationId = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly string ApiKeyPurpose = IntegrationSecretPurpose.For(IntegrationId, IntegrationSecretField.ApiKey);

    private readonly AesGcmEncryptionService _service = CreateService(TestKey);

    /// <summary>
    /// Values <see cref="AesGcmEncryptionService.TryDecrypt"/> must refuse. The last is a value this
    /// build made, re-labelled as a later format: it would decrypt, so only the version check can
    /// refuse it.
    /// </summary>
    public static TheoryData<string> MalformedValues => new()
    {
        "",
        "v1:",
        "v1:%%%",
        "a plain text secret",
        // 27 bytes: one short of a nonce and a tag with nothing between them.
        "v1:" + Convert.ToBase64String(new byte[27]),
        "v2:" + CreateService(TestKey).Encrypt(Secret, ApiKeyPurpose)["v1:".Length..],
    };

    [Theory]
    [InlineData("an-ascii-api-key-0123456789")]
    [InlineData("пароль-з-кирилицею")]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void TryDecrypt_of_what_Encrypt_made_is_the_plaintext(string plaintext)
    {
        //Arrange
        var sealedValue = _service.Encrypt(plaintext, ApiKeyPurpose);

        //Act
        var opened = _service.TryDecrypt(sealedValue, ApiKeyPurpose, out var decrypted);

        //Assert
        Assert.True(opened);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_twice_gives_two_different_values_that_both_decrypt()
    {
        //Arrange

        //Act
        var first = _service.Encrypt(Secret, ApiKeyPurpose);
        var second = _service.Encrypt(Secret, ApiKeyPurpose);

        //Assert
        // A fresh nonce per call: equal secrets must not be recognisable as equal in the database.
        Assert.NotEqual(first, second);
        Assert.True(_service.TryDecrypt(first, ApiKeyPurpose, out var firstPlaintext));
        Assert.True(_service.TryDecrypt(second, ApiKeyPurpose, out var secondPlaintext));
        Assert.Equal(Secret, firstPlaintext);
        Assert.Equal(Secret, secondPlaintext);
    }

    [Fact]
    public void Encrypt_starts_with_the_format_version_and_never_contains_the_plaintext()
    {
        //Arrange

        //Act
        var sealedValue = _service.Encrypt(Secret, ApiKeyPurpose);

        //Assert
        Assert.StartsWith("v1:", sealedValue, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, sealedValue, StringComparison.OrdinalIgnoreCase);
        // Nonce, ciphertext of the same length as the UTF-8 plaintext, tag.
        Assert.Equal(12 + Secret.Length + 16, Convert.FromBase64String(sealedValue["v1:".Length..]).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(12 + 31)]
    [InlineData(12 + 32)]
    [InlineData(12 + 32 + 15)]
    public void TryDecrypt_of_a_value_with_any_byte_changed_is_false(int index)
    {
        //Arrange
        // 0 and 11: the nonce; 12 and 43: the ciphertext; 44 and 59: the tag.
        var bytes = Convert.FromBase64String(_service.Encrypt(Secret, ApiKeyPurpose)["v1:".Length..]);
        bytes[index] ^= 0x01;
        var altered = "v1:" + Convert.ToBase64String(bytes);

        //Act
        var opened = _service.TryDecrypt(altered, ApiKeyPurpose, out var plaintext);

        //Assert
        Assert.False(opened);
        Assert.Null(plaintext);
    }

    [Fact]
    public void TryDecrypt_with_another_key_is_false()
    {
        //Arrange
        var sealedValue = CreateService(OtherKey).Encrypt(Secret, ApiKeyPurpose);

        //Act
        var opened = _service.TryDecrypt(sealedValue, ApiKeyPurpose, out var plaintext);

        //Assert
        Assert.False(opened);
        Assert.Null(plaintext);
    }

    // MAM-7, ADR 0007 — the associated data names the row and the column: a value copied to another
    // integration, or into the other secret's column, must not open there.
    [Theory]
    [InlineData("7c9e6679-7425-40de-944b-e07fc1f90ae7", IntegrationSecretField.Password)]
    [InlineData("0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0", IntegrationSecretField.ApiKey)]
    public void TryDecrypt_with_other_associated_data_is_false(string otherIntegrationId, IntegrationSecretField otherField)
    {
        //Arrange
        var sealedValue = _service.Encrypt(Secret, ApiKeyPurpose);
        var otherPurpose = IntegrationSecretPurpose.For(Guid.Parse(otherIntegrationId), otherField);

        //Act
        var opened = _service.TryDecrypt(sealedValue, otherPurpose, out var plaintext);

        //Assert
        Assert.False(opened);
        Assert.Null(plaintext);
    }

    [Theory]
    [MemberData(nameof(MalformedValues))]
    public void TryDecrypt_of_malformed_input_is_false(string protectedValue)
    {
        //Arrange

        //Act
        var opened = _service.TryDecrypt(protectedValue, ApiKeyPurpose, out var plaintext);

        //Assert
        Assert.False(opened);
        Assert.Null(plaintext);
    }

    [Theory]
    [InlineData(nameof(IntegrationConnectionEntity.PasswordEncrypted))]
    [InlineData(nameof(IntegrationConnectionEntity.ApiKeyEncrypted))]
    public void Encrypt_of_the_longest_accepted_secret_fits_its_column(string column)
    {
        //Arrange
        // Three UTF-8 bytes for each UTF-16 character, the most any character of a string takes per
        // char (a four-byte one is two chars): the longest sealed value a secret the rules accept can
        // give.
        var longest = new string('€', IntegrationSecretRules.MaximumLength);
        var columnLength = typeof(IntegrationConnectionEntity).GetProperty(column)!.GetCustomAttribute<MaxLengthAttribute>()!.Length;

        //Act
        var sealedValue = _service.Encrypt(longest, ApiKeyPurpose);

        //Assert
        Assert.True(sealedValue.Length <= columnLength, $"{sealedValue.Length} characters don't fit a column of {columnLength}.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not base64 at all!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]
    public void Constructing_the_service_with_a_bad_key_does_not_throw(string? key)
    {
        //Arrange

        //Act
        // L.2 — the key is read on first use, like the JWT secret: resolving the service (a DI
        // build, `dotnet ef`) must never need a working one.
        var exception = Record.Exception(() => CreateService(key));

        //Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Encrypt_with_a_bad_key_throws_the_key_sentence()
    {
        //Arrange
        var service = CreateService("AAECAwQFBgcICQoLDA0ODw==");

        //Act
        var exception = Record.Exception(() => service.Encrypt(Secret, ApiKeyPurpose));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.StartsWith("Encryption:Key is 16 bytes long, but AES-256 needs exactly 32.", exception.Message, StringComparison.Ordinal);
    }

    // A broken key is the operator's to fix, never "this secret is unreadable": that answer would
    // send the owner re-entering every credential, whatever the stored value looks like.
    [Theory]
    [InlineData("v1:AAAA")]
    [InlineData("not a sealed value")]
    public void TryDecrypt_with_a_bad_key_throws_the_key_sentence_whatever_the_value(string protectedValue)
    {
        //Arrange
        var service = CreateService("");

        //Act
        var exception = Record.Exception(() => service.TryDecrypt(protectedValue, ApiKeyPurpose, out _));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.StartsWith("Encryption:Key has no value", exception.Message, StringComparison.Ordinal);
    }

    private static AesGcmEncryptionService CreateService(string? key) =>
        new(Options.Create(new EncryptionOptions { Key = key }));
}
