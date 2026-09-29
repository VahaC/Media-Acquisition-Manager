using System.Diagnostics;
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
/// and moved into place, and only one process at a time may do that: creating a secret happens under
/// an exclusive lock on <see cref="LockFileName"/>, and whoever gets the lock after another process
/// published the file reads that file instead of writing its own. On Unix the directory is 0700 and
/// each file 0600, set at creation so a key is never readable by others, not even for a moment.
/// Windows has no such modes; there the ACL inherited from the data directory protects the files.
/// </remarks>
internal sealed class FirstRunSecretStore(string secretsDirectory)
{
    /// <summary>
    /// The lock file a first start creates the secrets under. It stays, empty, once created: deleting
    /// it would let a process that opened the old file and one that creates a new one both hold "the"
    /// lock.
    /// </summary>
    public const string LockFileName = ".creating.lock";

    private const UnixFileMode OwnerOnlyDirectory =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// How long a start waits for another process that is creating the secrets. Generating and writing
    /// one takes milliseconds, so a wait this long means that process is stuck; failing with a message
    /// beats hanging before the app ever listens.
    /// </summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How often a locked lock file is tried again.</summary>
    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>The value on disk, or a newly generated one that is now on disk.</summary>
    /// <exception cref="InvalidOperationException">The file exists but holds nothing, or another
    /// process has held the lock for longer than <see cref="LockTimeout"/>.</exception>
    public string GetOrCreate(FirstRunSecret secret)
    {
        var path = Path.Combine(secretsDirectory, secret.FileName);
        if (File.Exists(path))
            return ReadExisting(path, secret);

        EnsureDirectory();
        using var creationLock = AcquireCreationLock();

        // Another process may have published the file while this one waited for the lock: its value is
        // the installation's secret now.
        if (File.Exists(path))
            return ReadExisting(path, secret);

        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(secret.ByteLength));
        var temporaryPath = Path.Combine(secretsDirectory, $".{secret.FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteOwnerOnly(temporaryPath, value);
            // Without overwrite, but the lock is what makes this safe: on Linux a move without
            // overwrite is not atomic, and two racing moves both succeeded, the later one replacing
            // the earlier file — a caller then ran with a key the disk no longer had. The race test
            // failed 23 runs in 40 in a Linux container before the lock.
            File.Move(temporaryPath, path, overwrite: false);
            return value;
        }
        catch (IOException) when (File.Exists(path))
        {
            // A process that creates the file without the lock (an older build of the app) got there
            // first. Its value is already the installation's secret, so use it and drop ours.
            return ReadExisting(path, secret);
        }
        finally
        {
            // Gone already after a successful move; File.Delete ignores a missing file.
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Opens <see cref="LockFileName"/> exclusively, waiting while another process (or another store
    /// in this one) holds it. The lock ends when the returned stream is disposed, or when the process
    /// dies — a crashed first start never leaves the next one waiting.
    /// </summary>
    /// <remarks>
    /// <see cref="FileShare.None"/> is a sharing mode on Windows and an advisory <c>flock</c> on Unix;
    /// either way a second exclusive open fails with an <see cref="IOException"/> until the first is
    /// closed, across processes and within one alike.
    /// </remarks>
    private FileStream AcquireCreationLock()
    {
        var lockPath = Path.Combine(secretsDirectory, LockFileName);
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerOnlyFile;

        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, options);
            }
            catch (IOException exception)
            {
                if (Stopwatch.GetElapsedTime(startedAt) >= LockTimeout)
                {
                    throw new InvalidOperationException(
                        $"Another process has been creating the secrets in {secretsDirectory} for more than " +
                        $"{LockTimeout.TotalSeconds:0} seconds, or holds {lockPath} open. Stop the other " +
                        "instance of the app that uses this data directory, then start this one again.",
                        exception);
                }

                Thread.Sleep(LockRetryInterval);
            }
        }
    }

    private static string ReadExisting(string path, FirstRunSecret secret)
    {
        // FileShare.Delete, which File.ReadAllText doesn't pass: on Windows the process whose move
        // just published this file still holds it open with delete access for a moment after the
        // rename, and an open that doesn't share delete fails then with a sharing violation. The
        // callers that lost the move race read at exactly that moment, so a concurrent first start
        // failed roughly once in 300 races. Sharing delete weakens nothing: the file only ever
        // appears whole, through the move, and is never written in place.
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
        // Trimmed, so a file an operator wrote with `openssl rand -base64 32 > file` (trailing
        // newline) holds the same value as one the app wrote.
        var value = reader.ReadToEnd().Trim();
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
