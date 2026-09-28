using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.Extensions.Configuration;

namespace MediaAcquisitionManager.Api.Tests.Security;

public sealed class FirstRunSecretsHttpTests
    : IClassFixture<FirstRunApiFactory>, IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    // The on-disk names are part of an installation's contract, so they are spelled out here
    // rather than read from the code under test: renaming one must fail this test.
    private const string SecretsDirectoryName = ".secrets";
    private const string EncryptionKeyFileName = "encryption-key";
    private const string JwtSecretFileName = "jwt-secret";

    private readonly FirstRunApiFactory _firstRun;
    private readonly MediaAcquisitionManagerApiFactory _preset;

    public FirstRunSecretsHttpTests(FirstRunApiFactory firstRun, MediaAcquisitionManagerApiFactory preset)
    {
        _firstRun = firstRun;
        _preset = preset;
    }

    // Resetting starts both hosts, which is when provisioning runs.
    public async ValueTask InitializeAsync()
    {
        await _firstRun.ResetAsync();
        await _preset.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task First_start_generates_a_32_byte_encryption_key()
    {
        //Arrange
        var path = Path.Combine(_firstRun.DataDirectory, SecretsDirectoryName, EncryptionKeyFileName);

        //Act
        var content = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(32, Convert.FromBase64String(content).Length);
    }

    [Fact]
    public async Task First_start_generates_a_64_byte_jwt_secret()
    {
        //Arrange
        var path = Path.Combine(_firstRun.DataDirectory, SecretsDirectoryName, JwtSecretFileName);

        //Act
        var content = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(64, Convert.FromBase64String(content).Length);
    }

    [Fact]
    public async Task First_start_serves_exactly_the_file_contents_through_configuration()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var secretsDirectory = Path.Combine(_firstRun.DataDirectory, SecretsDirectoryName);
        var encryptionKey = await File.ReadAllTextAsync(Path.Combine(secretsDirectory, EncryptionKeyFileName), cancellationToken);
        var jwtSecret = await File.ReadAllTextAsync(Path.Combine(secretsDirectory, JwtSecretFileName), cancellationToken);

        //Act
        var configuration = _firstRun.Resolve<IConfiguration>();

        //Assert
        Assert.Equal(encryptionKey, configuration["Encryption:Key"]);
        Assert.Equal(jwtSecret, configuration["Jwt:Secret"]);
    }

    [Fact]
    public void Start_with_configured_secrets_uses_them_and_writes_no_secret_files()
    {
        //Arrange
        var secretsDirectory = Path.Combine(_preset.DataDirectory, SecretsDirectoryName);

        //Act
        var configuration = _preset.Resolve<IConfiguration>();

        //Assert
        Assert.Equal(MediaAcquisitionManagerApiFactory.TestEncryptionKey, configuration["Encryption:Key"]);
        Assert.Equal(MediaAcquisitionManagerApiFactory.TestJwtSecret, configuration["Jwt:Secret"]);
        Assert.False(Directory.Exists(secretsDirectory));
    }
}
