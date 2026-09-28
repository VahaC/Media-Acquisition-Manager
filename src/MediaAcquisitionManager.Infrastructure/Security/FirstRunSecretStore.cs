using System.Security.Cryptography;
using System.Text;

namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>
/// Reads a first-run secret from <paramref name="secretsDirectory"/>, generating it the first time.
/// A value that exists is never replaced.
/// </summary>
/// <remarks>
/// Never overwriting is the whole point: data encrypted with the old key would become unreadable
/// and every session signed with the old secret would end. So a new value is written to a temp file
/// and moved into place without overwrite — if another process starting against the same data
/// directory won that race, its value is the one on disk and the one both processes use. On Unix the
/// directory is 0700 and each file 0600, set at creation so a key is never readable by others, not
/// even for a moment. Windows has no such modes; there the ACL inherited from the data directory
/// protects the files.
/// </remarks>
internal sealed class FirstRunSecretStore(string secretsDirectory)
{
    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>The value on disk, or a newly generated one that is now on disk.</summary>
    /// <exception cref="InvalidOperationException">The file exists but holds nothing.</exception>
    public string GetOrCreate(FirstRunSecret secret)
    {
        var path = Path.Combine(secretsDirectory, secret.FileName);
        if (File.Exists(path))
            return ReadExisting(path, secret);

        EnsureDirectory();
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(secret.ByteLength));
        var temporaryPath = Path.Combine(secretsDirectory, $".{secret.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteOwnerOnly(temporaryPath, value);
            File.Move(temporaryPath, path, overwrite: false);
            return value;
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another process created the file between our existence check and the move. Its
            // value is already the installation's secret, so use it and drop ours.
            return ReadExisting(path, secret);
        }
        finally
        {
            // Gone already after a successful move; File.Delete ignores a missing file.
            File.Delete(temporaryPath);
        }
    }

    private static string ReadExisting(string path, FirstRunSecret secret)
    {
        // Trimmed, so a file an operator wrote with `openssl rand -base64 32 > file` (trailing
        // newline) holds the same value as one the app wrote.
        var value = File.ReadAllText(path).Trim();
        if (value.Length > 0)
            return value;

        // Silently generating a replacement would look like a fix and quietly cost whatever the old
        // value protected, so refuse to start and say what the operator can do instead.
        throw new InvalidOperationException(
            $"The secret file {path} is empty, so {secret.ConfigurationKey} has no value. Restore the " +
            $"file from a backup or set {secret.ConfigurationKey} explicitly. Deleting the file makes " +
            $"the app generate a new value, which {secret.LossConsequence}.");
    }

    private void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(secretsDirectory);
            return;
        }

        Directory.CreateDirectory(secretsDirectory, OwnerOnlyDirectory);
        // CreateDirectory leaves an existing directory's mode alone; tighten it anyway, so one made
        // by hand with the usual 0755 doesn't expose the files about to be written into it.
        File.SetUnixFileMode(secretsDirectory, OwnerOnlyDirectory);
    }

    private static void WriteOwnerOnly(string path, string value)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerOnlyFile;

        using var stream = new FileStream(path, options);
        stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(value));
        // On disk before the move publishes it: a crash must not leave a secret file that exists
        // but is empty, which the next start would refuse.
        stream.Flush(flushToDisk: true);
    }
}
