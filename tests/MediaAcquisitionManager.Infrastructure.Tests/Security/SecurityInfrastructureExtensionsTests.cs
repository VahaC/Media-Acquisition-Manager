using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

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
}
