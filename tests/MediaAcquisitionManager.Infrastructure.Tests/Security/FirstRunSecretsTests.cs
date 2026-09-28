using System.Runtime.Versioning;
using MediaAcquisitionManager.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace MediaAcquisitionManager.Infrastructure.Tests.Security;

public sealed class FirstRunSecretsTests : IDisposable
{
    private const string EncryptionKey = "Encryption:Key";
    private const string JwtSecret = "Jwt:Secret";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("mam-first-run-secrets-");

    /// <summary>For <c>SkipWhen</c>: Unix file modes don't exist on Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    // The directory the app would use, not created up front: a first start meets no .secrets at all.
    private string SecretsDirectory => Path.Combine(_root.FullName, ".secrets");

    private string EncryptionKeyFile => Path.Combine(SecretsDirectory, "encryption-key");

    private string JwtSecretFile => Path.Combine(SecretsDirectory, "jwt-secret");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void AddFirstRunSecrets_on_a_first_start_generates_both_files_with_their_sizes()
    {
        //Arrange
        var configuration = new ConfigurationManager();

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal(32, Convert.FromBase64String(File.ReadAllText(EncryptionKeyFile)).Length);
        Assert.Equal(64, Convert.FromBase64String(File.ReadAllText(JwtSecretFile)).Length);
    }

    [Fact]
    public void AddFirstRunSecrets_on_a_first_start_serves_exactly_the_file_contents()
    {
        //Arrange
        var configuration = new ConfigurationManager();

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal(File.ReadAllText(EncryptionKeyFile), configuration[EncryptionKey]);
        Assert.Equal(File.ReadAllText(JwtSecretFile), configuration[JwtSecret]);
    }

    [Fact]
    public void AddFirstRunSecrets_with_existing_files_reuses_them_and_never_rewrites_them()
    {
        //Arrange
        Directory.CreateDirectory(SecretsDirectory);
        File.WriteAllText(EncryptionKeyFile, "existing-encryption-key");
        File.WriteAllText(JwtSecretFile, "existing-jwt-secret");
        var writtenAt = File.GetLastWriteTimeUtc(EncryptionKeyFile);
        var configuration = new ConfigurationManager();

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal("existing-encryption-key", configuration[EncryptionKey]);
        Assert.Equal("existing-jwt-secret", configuration[JwtSecret]);
        Assert.Equal("existing-encryption-key", File.ReadAllText(EncryptionKeyFile));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(EncryptionKeyFile));
    }

    [Fact]
    public void AddFirstRunSecrets_trims_the_trailing_newline_a_hand_written_file_ends_with()
    {
        //Arrange
        Directory.CreateDirectory(SecretsDirectory);
        File.WriteAllText(EncryptionKeyFile, "hand-written-key\n");
        var configuration = new ConfigurationManager();

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal("hand-written-key", configuration[EncryptionKey]);
    }

    [Fact]
    public void GetOrCreate_racing_on_an_empty_directory_leaves_one_value_that_every_caller_uses()
    {
        //Arrange
        const int callers = 16;
        var values = new string[callers];

        //Act
        // A store per caller, like separate processes starting against one data directory.
        Parallel.For(0, callers, index =>
            values[index] = new FirstRunSecretStore(SecretsDirectory).GetOrCreate(FirstRunSecrets.EncryptionKey));

        //Assert
        var onDisk = File.ReadAllText(EncryptionKeyFile);
        Assert.All(values, value => Assert.Equal(onDisk, value));
        Assert.Equal([EncryptionKeyFile], Directory.GetFiles(SecretsDirectory));
    }

    [Fact]
    public void AddFirstRunSecrets_with_both_secrets_configured_keeps_them_and_writes_nothing()
    {
        //Arrange
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [EncryptionKey] = "configured-encryption-key",
            [JwtSecret] = "configured-jwt-secret",
        });

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal("configured-encryption-key", configuration[EncryptionKey]);
        Assert.Equal("configured-jwt-secret", configuration[JwtSecret]);
        Assert.False(Directory.Exists(SecretsDirectory));
    }

    [Fact]
    public void AddFirstRunSecrets_with_one_secret_configured_generates_only_the_other()
    {
        //Arrange
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [JwtSecret] = "configured-jwt-secret" });

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal("configured-jwt-secret", configuration[JwtSecret]);
        Assert.False(File.Exists(JwtSecretFile));
        Assert.Equal(File.ReadAllText(EncryptionKeyFile), configuration[EncryptionKey]);
    }

    [Fact]
    public void AddFirstRunSecrets_treats_a_whitespace_value_as_not_configured()
    {
        //Arrange
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { [EncryptionKey] = "   " });

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal(File.ReadAllText(EncryptionKeyFile), configuration[EncryptionKey]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n")]
    public void AddFirstRunSecrets_with_an_empty_secret_file_refuses_to_start_and_says_what_to_do(string content)
    {
        //Arrange
        Directory.CreateDirectory(SecretsDirectory);
        File.WriteAllText(EncryptionKeyFile, content);
        var configuration = new ConfigurationManager();

        //Act
        var exception = Record.Exception(() => configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory));

        //Assert
        var refusal = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            $"The secret file {EncryptionKeyFile} is empty, so Encryption:Key has no value. Restore the file " +
            "from a backup or set Encryption:Key explicitly. Deleting the file makes the app generate a new " +
            "value, which makes every credential stored with the old key unreadable.",
            refusal.Message);
        Assert.Equal(content, File.ReadAllText(EncryptionKeyFile));
    }

    [Fact]
    public void AddFirstRunSecrets_with_an_empty_jwt_secret_file_says_a_new_value_signs_nobody_out()
    {
        //Arrange
        Directory.CreateDirectory(SecretsDirectory);
        File.WriteAllText(JwtSecretFile, "");
        var configuration = new ConfigurationManager();

        //Act
        var exception = Record.Exception(() => configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory));

        //Assert
        // MAM-4 — the refresh cookie is a random value checked against the session table, not signed
        // with this key: an operator who rotates it to evict someone must not be told it does.
        var refusal = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            $"The secret file {JwtSecretFile} is empty, so Jwt:Secret has no value. Restore the file " +
            "from a backup or set Jwt:Secret explicitly. Deleting the file makes the app generate a new " +
            "value, which makes every access token already issued stop working; open tabs quietly get a " +
            "new one from their refresh cookie, and nobody is signed out.",
            refusal.Message);
    }

    [Fact(Skip = "Unix file modes do not exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public void AddFirstRunSecrets_on_Unix_keeps_the_directory_and_the_files_owner_only()
    {
        //Arrange
        var configuration = new ConfigurationManager();

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(SecretsDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(EncryptionKeyFile));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(JwtSecretFile));
    }

    [Fact(Skip = "Unix file modes do not exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public void AddFirstRunSecrets_on_Unix_tightens_an_existing_world_readable_secrets_directory()
    {
        //Arrange
        // Made by hand with the usual 0755, before the app ever ran. Set explicitly after creating,
        // so the umask can't make the starting mode stricter than the test means it to be.
        Directory.CreateDirectory(SecretsDirectory);
        File.SetUnixFileMode(
            SecretsDirectory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var configuration = new ConfigurationManager();

        //Act
        configuration.AddMediaAcquisitionManagerFirstRunSecrets(SecretsDirectory);

        //Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(SecretsDirectory));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(EncryptionKeyFile));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(JwtSecretFile));
    }
}
