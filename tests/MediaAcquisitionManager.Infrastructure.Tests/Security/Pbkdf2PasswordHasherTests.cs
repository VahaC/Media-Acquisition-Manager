using MediaAcquisitionManager.Infrastructure.Security;

namespace MediaAcquisitionManager.Infrastructure.Tests.Security;

public sealed class Pbkdf2PasswordHasherTests
{
    // A low count keeps the tests fast; the algorithm and the format are the production ones.
    private const int Iterations = 1_000;
    private const string Password = "correct-horse-battery";

    private readonly Pbkdf2PasswordHasher _hasher = new(Iterations);

    [Fact]
    public void Verify_of_the_hashed_password_is_true()
    {
        //Arrange
        var hash = _hasher.Hash(Password);

        //Act
        var matches = _hasher.Verify(Password, hash);

        //Assert
        Assert.True(matches);
    }

    [Theory]
    [InlineData("correct-horse-batterY")]
    [InlineData("correct-horse-battery ")]
    [InlineData("correct-horse")]
    [InlineData("")]
    public void Verify_of_another_password_is_false(string attempt)
    {
        //Arrange
        var hash = _hasher.Hash(Password);

        //Act
        var matches = _hasher.Verify(attempt, hash);

        //Assert
        Assert.False(matches);
    }

    [Fact]
    public void Hash_of_the_same_password_twice_uses_a_fresh_salt()
    {
        //Arrange

        //Act
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        //Assert
        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Split('$')[2], second.Split('$')[2]);
        Assert.True(_hasher.Verify(Password, first));
        Assert.True(_hasher.Verify(Password, second));
    }

    [Fact]
    public void Hash_stores_the_algorithm_the_iteration_count_a_16_byte_salt_and_a_32_byte_hash()
    {
        //Arrange

        //Act
        var hash = _hasher.Hash(Password);

        //Assert
        // The stored format is part of every installation's data: changing it strands every
        // password already saved.
        var parts = hash.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("pbkdf2-sha256", parts[0]);
        Assert.Equal("1000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
    }

    [Fact]
    public void Hash_never_contains_the_password()
    {
        //Arrange

        //Act
        var hash = _hasher.Hash(Password);

        //Assert
        Assert.DoesNotContain(Password, hash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_uses_the_iteration_count_stored_in_the_hash()
    {
        //Arrange
        // Made before the count was changed: it must keep working after an upgrade raises it.
        var olderHash = new Pbkdf2PasswordHasher(2_000).Hash(Password);

        //Act
        var matches = _hasher.Verify(Password, olderHash);

        //Assert
        Assert.StartsWith("pbkdf2-sha256$2000$", olderHash, StringComparison.Ordinal);
        Assert.True(matches);
    }

    [Fact]
    public void Verify_of_a_hash_whose_iteration_count_was_changed_is_false()
    {
        //Arrange
        var parts = _hasher.Hash(Password).Split('$');
        var tampered = string.Join('$', parts[0], "999", parts[2], parts[3]);

        //Act
        var matches = _hasher.Verify(Password, tampered);

        //Assert
        Assert.False(matches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$1000$c2FsdHNhbHRzYWx0c2FsdA==")]
    [InlineData("pbkdf2-sha256$1000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaA==$extra")]
    [InlineData("bcrypt$1000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$lots$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$0$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$-5$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$ 1000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$1000$!!not base64!!$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$1000$c2FsdHNhbHRzYWx0c2FsdA==$!!not base64!!")]
    [InlineData("pbkdf2-sha256$1000$$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=")]
    [InlineData("pbkdf2-sha256$1000$c2FsdHNhbHRzYWx0c2FsdA==$")]
    public void Verify_of_a_malformed_hash_is_false_without_throwing(string hash)
    {
        //Arrange
        var matches = true;

        //Act
        var exception = Record.Exception(() => matches = _hasher.Verify(Password, hash));

        //Assert
        Assert.Null(exception);
        Assert.False(matches);
    }
}
