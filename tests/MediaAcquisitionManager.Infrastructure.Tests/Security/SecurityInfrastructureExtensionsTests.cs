using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.Infrastructure.Tests.Security;

public sealed class SecurityInfrastructureExtensionsTests
{
    [Fact]
    public void AddSecurityInfrastructure_registers_a_hasher_with_600000_iterations()
    {
        //Arrange
        using var services = new ServiceCollection().AddSecurityInfrastructure().BuildServiceProvider();
        var hasher = services.GetRequiredService<IPasswordHasher>();

        //Act
        // One hash at the production cost, a few hundred milliseconds: the price of pinning it.
        var hash = hasher.Hash("correct-horse-battery");

        //Assert
        // MAM-4 — every other test hashes at a cheap count (the test host swaps the hasher, the
        // hasher tests build their own), so nothing else would notice production hashes becoming
        // hundreds of times cheaper to brute-force offline.
        Assert.StartsWith("pbkdf2-sha256$600000$", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void AddSecurityInfrastructure_registers_the_aes_gcm_encryption_service()
    {
        //Arrange
        const string key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";
        const string purpose = "integration-connection/00000000000000000000000000000001/api-key";
        using var services = new ServiceCollection()
            .Configure<EncryptionOptions>(options => options.Key = key)
            .AddSecurityInfrastructure()
            .BuildServiceProvider();
        var sameKey = new AesGcmEncryptionService(Options.Create(new EncryptionOptions { Key = key }));

        //Act
        var encryption = services.GetRequiredService<IEncryptionService>();
        var sealedValue = encryption.Encrypt("an-api-key-value", purpose);

        //Assert
        Assert.IsType<AesGcmEncryptionService>(encryption);
        // A singleton: the key is parsed once per process, not once per request.
        Assert.Same(encryption, services.GetRequiredService<IEncryptionService>());
        // It seals with the configured key, not some other one.
        Assert.True(sameKey.TryDecrypt(sealedValue, purpose, out var plaintext));
        Assert.Equal("an-api-key-value", plaintext);
    }
}
