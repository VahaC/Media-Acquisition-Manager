using System.Runtime.Versioning;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-19 — the hash of a file's contents on real files in a temp folder (ADR 0022): every byte through SHA-256,
// what sha256sum prints, however many chunks the read takes; one digest for two names of one file and for two
// copies, told apart by their identity; every refusal of Describe before a byte is read (ADR 0009); a file
// that changed while it was read — grew, was rewritten, grew and shrank back — changed while reading, never a
// hash, and a change wins over a read that failed meanwhile; a cancelled hash throws and reads no further. A
// test reads in chunks of a few bytes and acts between them through the inventory's AfterHashChunk hook. A pipe
// and a path swapped after the open, which only Linux can show, are in LocalFileInventoryHashLinuxTests; the
// inspection run that calls this, in Api.Tests.
public sealed class LocalFileInventoryHashTests : IDisposable
{
    /// <summary>A few bytes per read, so a file of a few bytes is read in several chunks and a test can act
    /// between them.</summary>
    private const int SmallChunkBytes = 4;

    /// <summary>The digest sha256sum prints for no bytes at all.</summary>
    private const string Sha256OfNothing = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private const string QuickBrownFox = "The quick brown fox jumps over the lazy dog";

    /// <summary>The digest sha256sum prints for <see cref="QuickBrownFox"/>: the well-known one, so the hash is
    /// pinned against more than .NET's own SHA-256.</summary>
    private const string Sha256OfQuickBrownFox = "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592";

    private const string TenBytes = "0123456789";

    private const string NoAllowedRoots = "No media folder is allowed yet, so the app reads no file.";

    private static readonly DateTime Written = new DateTime(2026, 9, 30, 12, 34, 56, 789, DateTimeKind.Utc).AddTicks(1_234);

    private readonly TemporaryFolder _folder = new("mam-file-hash-");

    /// <summary>Every count of bytes read so far the hook was given: one entry per chunk read.</summary>
    private readonly List<long> _chunksRead = [];

    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryHashTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    /// <summary>For <c>SkipUnless</c>: Linux and Windows report a device and an inode.</summary>
    public static bool ReportsIdentity => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    /// <summary>For <c>SkipWhen</c>: Unix file modes don't exist on Windows; for <c>SkipUnless</c>: only Windows
    /// refuses a read in a range another handle has locked.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Hash_of_a_file_inside_the_root_is_the_sha256_of_its_bytes_with_its_facts()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        File.SetLastWriteTimeUtc(movie, Written);
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, movie, _media);
        var described = await inventory.DescribeAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(Sha256Of(movie), result.Value!.Sha256);
        // Lowercase hex, as sha256sum prints it: the form stored and compared.
        Assert.Matches("^[0-9a-f]{64}$", result.Value.Sha256);
        // The key the hash is kept under: the facts Describe gives, the modification time to the tick.
        Assert.Equal(described.Value, result.Value.File);
        Assert.Equal(Written, result.Value.File.ModifiedUtc);
    }

    [Fact]
    public async Task Hash_of_an_empty_file_is_the_sha256_of_nothing()
    {
        //Arrange
        var empty = _folder.WriteFile("", "media", "empty.mkv");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, empty, _media);

        //Assert
        // Read to its end at once: no chunk, and the end where the facts say it is.
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(Sha256OfNothing, result.Value!.Sha256);
        Assert.Equal(0, result.Value.File.SizeBytes);
        Assert.Empty(_chunksRead);
    }

    [Fact]
    public async Task Hash_read_in_chunks_smaller_than_the_file_gives_the_same_digest()
    {
        //Arrange
        var movie = _folder.WriteFile(QuickBrownFox, "media", "movie.mkv");
        var inventory = Inventory(SmallChunkBytes);

        //Act
        var result = await HashAsync(inventory, movie, _media);
        var whole = await HashAsync(new LocalFileInventory(new FfprobeProcess()), movie, _media);

        //Assert
        // 43 bytes in ten chunks of 4 and one of 3: the digest doesn't depend on where the reads cut the file.
        long[] chunks = [4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 43];
        Assert.Equal(chunks, _chunksRead);
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(Sha256OfQuickBrownFox, result.Value!.Sha256);
        Assert.Equal(whole.Value, result.Value);
    }

    [Fact]
    public async Task Hash_reads_a_large_file_in_chunks_of_one_mebibyte()
    {
        //Arrange
        // Two and a half mebibytes of a pattern that repeats every 251 bytes, so no chunk is like another.
        var content = new byte[5 << 19];
        for (var index = 0; index < content.Length; index++)
            content[index] = (byte)(index % 251);
        var movie = _folder.PathOf("media", "movie.mkv");
        await File.WriteAllBytesAsync(movie, content, TestContext.Current.CancellationToken);
        // The chunk size the app reads with: left as it is.
        var inventory = new LocalFileInventory(new FfprobeProcess()) { AfterHashChunk = _chunksRead.Add };

        //Act
        var result = await HashAsync(inventory, movie, _media);

        //Assert
        // The app's own chunk: one long sequential read from a spinning disk per mebibyte.
        long[] chunks = [1 << 20, 2 << 20, 5 << 19];
        Assert.Equal(chunks, _chunksRead);
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), result.Value!.Sha256);
    }

    [Fact(Skip = "Only Linux and Windows report a file's identity.", SkipUnless = nameof(ReportsIdentity))]
    public async Task Hash_of_two_hard_linked_names_is_one_digest_with_one_identity()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var secondName = Path.Combine(_media, "the same movie.mkv");
        FileLinks.CreateHardLink(movie, secondName);
        var inventory = Inventory();

        //Act
        var first = await HashAsync(inventory, movie, _media);
        var second = await HashAsync(inventory, secondName, _media);

        //Assert
        // One file under two names: one key, so the run stores one hash for both and counts its space once.
        Assert.Equal(FileInventoryStatus.Found, second.Status);
        Assert.Equal(first.Value, second.Value);
        Assert.NotNull(first.Value!.File.DeviceId);
        Assert.NotNull(first.Value.File.Inode);
        Assert.Equal(2, first.Value.File.LinkCount);
    }

    [Fact(Skip = "Only Linux and Windows report a file's identity.", SkipUnless = nameof(ReportsIdentity))]
    public async Task Hash_of_two_copies_is_one_digest_with_two_identities()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var copy = Path.Combine(_media, "a copy.mkv");
        File.Copy(movie, copy);
        var inventory = Inventory();

        //Act
        var original = (await HashAsync(inventory, movie, _media)).Value!;
        var copied = (await HashAsync(inventory, copy, _media)).Value!;

        //Assert
        // What the hash is for: byte-identical files that are not one file, which only their identity told apart.
        Assert.Equal(original.Sha256, copied.Sha256);
        Assert.Equal(original.File.DeviceId, copied.File.DeviceId);
        Assert.NotEqual(original.File.Inode, copied.File.Inode);
    }

    [Fact]
    public async Task Hash_through_a_folder_link_inside_the_root_reads_the_file_behind_it()
    {
        //Arrange
        var real = _folder.WriteFile("the real movie", "media", "real", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "linked"), Path.Combine(_media, "real"));
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, Path.Combine(_media, "linked", "movie.mkv"), _media);
        var direct = await HashAsync(inventory, real, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(Sha256Of(real), result.Value!.Sha256);
        Assert.Equal(direct.Value, result.Value);
    }

    [Fact]
    public async Task Hash_of_a_file_outside_every_root_reads_nothing()
    {
        //Arrange
        var secret = _folder.WriteFile("secret", "outside", "secret.txt");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, secret, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"“{secret}” is outside the media folders the app may read.", TestTexts.En(result.Error));
        Assert.Empty(_chunksRead);
    }

    [Fact]
    public async Task Hash_with_no_allowed_root_reads_nothing()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, movie);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(NoAllowedRoots, TestTexts.En(result.Error));
        Assert.Empty(_chunksRead);
    }

    [Fact]
    public async Task Hash_through_a_folder_link_leading_outside_the_root_reads_nothing()
    {
        //Arrange
        var secret = _folder.WriteFile("secret", "outside", "secret.txt");
        FileLinks.CreateFolderLink(Path.Combine(_media, "escape"), _outside);
        var throughLink = Path.Combine(_media, "escape", "secret.txt");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, throughLink, _media);

        //Assert
        // The hash reads every byte: through a link leading out it would read the app's own data whole.
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(
            $"“{throughLink}” leads through a link to “{secret}”, outside the media folders the app may read.",
            TestTexts.En(result.Error));
        Assert.Empty(_chunksRead);
    }

    [Fact]
    public async Task Hash_of_a_missing_file_is_not_found()
    {
        //Arrange
        var missing = Path.Combine(_media, "missing.mkv");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, missing, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"There is no file at “{missing}”.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Hash_of_a_folder_is_not_a_regular_file()
    {
        //Arrange
        var folder = _folder.CreateFolder("media", "Season 1");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, folder, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotARegularFile, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"“{folder}” is a folder, not a file.", TestTexts.En(result.Error));
        Assert.Empty(_chunksRead);
    }

    [Fact]
    public async Task Hash_of_a_file_that_grows_while_it_is_read_is_changed_while_reading()
    {
        //Arrange
        // A download client still writing: bytes arrive after the first chunk was read.
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        var inventory = Inventory(SmallChunkBytes, OnFirstChunk(() => File.AppendAllText(movie, "and more")));

        //Act
        var result = await HashAsync(inventory, movie, _media);

        //Assert
        // What was read belongs to neither version, so nothing may be kept under either key.
        Assert.Equal(FileInventoryStatus.ChangedWhileReading, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(ChangedWhileHashing(movie), TestTexts.En(result.Error));
        Assert.Equal(TenBytes.Length + "and more".Length, new FileInfo(movie).Length);
    }

    [Fact]
    public async Task Hash_of_a_file_rewritten_to_the_same_length_with_another_modification_time_is_changed_while_reading()
    {
        //Arrange
        // The size alone can't tell: the file is rewritten in place, every byte, and only its modification time
        // moves.
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        File.SetLastWriteTimeUtc(movie, Written);
        var inventory = Inventory(SmallChunkBytes, OnFirstChunk(() =>
        {
            using (var writer = new FileStream(movie, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                writer.Write("ABCDEFGHIJ"u8);
            File.SetLastWriteTimeUtc(movie, Written.AddMinutes(1));
        }));

        //Act
        var result = await HashAsync(inventory, movie, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.ChangedWhileReading, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(ChangedWhileHashing(movie), TestTexts.En(result.Error));
        Assert.Equal(TenBytes.Length, new FileInfo(movie).Length);
    }

    [Fact]
    public async Task Hash_of_a_file_that_grows_and_shrinks_back_to_its_facts_while_it_is_read_is_changed_while_reading()
    {
        //Arrange
        // Bytes come after the first chunk, are read, and go again before the end, the modification time set back:
        // the facts asked after the read agree with those before it, and only where the read ended tells.
        const string appended = "abcde";
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        File.SetLastWriteTimeUtc(movie, Written);
        var inventory = Inventory(SmallChunkBytes, read =>
        {
            if (read == SmallChunkBytes)
            {
                File.AppendAllText(movie, appended);
            }
            else if (read == TenBytes.Length + appended.Length)
            {
                using (var writer = new FileStream(movie, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    writer.SetLength(TenBytes.Length);
                File.SetLastWriteTimeUtc(movie, Written);
            }
        });

        //Act
        var result = await HashAsync(inventory, movie, _media);
        var described = await inventory.DescribeAsync(movie, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.ChangedWhileReading, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(ChangedWhileHashing(movie), TestTexts.En(result.Error));
        // 15 bytes read from a file whose facts said 10, before the read and after it.
        Assert.Equal(TenBytes.Length + appended.Length, _chunksRead[^1]);
        Assert.Equal(((long)TenBytes.Length, Written), (described.Value!.SizeBytes, described.Value.ModifiedUtc));
    }

    [Fact]
    public async Task Hash_cancelled_before_it_starts_throws_and_reads_nothing()
    {
        //Arrange
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        var inventory = Inventory(SmallChunkBytes);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        var exception = await Record.ExceptionAsync(() => inventory.HashAsync(movie, [_media], cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Empty(_chunksRead);
    }

    [Fact]
    public async Task Hash_cancelled_while_it_reads_throws_and_reads_no_further()
    {
        //Arrange
        // The app stopping in the middle of a large file: the read ends at the next chunk, not at the file's end.
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        using var cancellation = new CancellationTokenSource();
        var inventory = Inventory(SmallChunkBytes, OnFirstChunk(cancellation.Cancel));

        //Act
        var exception = await Record.ExceptionAsync(() => inventory.HashAsync(movie, [_media], cancellation.Token));

        //Assert
        // Thrown, never an outcome: the run stays running for the reconciler, and nothing is stored.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        long[] chunks = [SmallChunkBytes];
        Assert.Equal(chunks, _chunksRead);
    }

    [Fact(Skip = "Unix file modes do not exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public async Task Hash_of_a_file_the_app_may_not_read_is_unreadable()
    {
        //Arrange
        var movie = _folder.WriteFile("12345", "media", "movie.mkv");
        File.SetUnixFileMode(movie, UnixFileMode.None);
        if (CanStillRead(movie))
            Assert.Skip("This process reads a file with mode 000 anyway (it runs as root), so it can't see the refusal.");
        var inventory = Inventory();

        //Act
        var result = await HashAsync(inventory, movie, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"The app isn't allowed to read “{movie}”.", TestTexts.En(result.Error));
        Assert.Empty(_chunksRead);
    }

    [Fact(Skip = "Only Windows refuses a read in a range another handle has locked; Linux locks are advisory.", SkipUnless = nameof(IsWindows))]
    [SupportedOSPlatform("windows")]
    public async Task Hash_of_a_file_whose_read_fails_is_unreadable_in_words_without_the_systems_own_path()
    {
        //Arrange
        // Another program holds a lock on the bytes: the open succeeds, the read itself fails.
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        using var holder = new FileStream(movie, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        holder.Lock(0, TenBytes.Length);
        var inventory = Inventory(SmallChunkBytes);

        //Act
        var result = await HashAsync(inventory, movie, _media);

        //Assert
        // An outcome with its reason, not an exception; the reason is the system's own, in its language, so only
        // its frame is pinned. .NET ends it with the handle's \\?\ path, which isn't the path the owner knows.
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
        Assert.StartsWith($"The app couldn't read “{movie}”: ", TestTexts.En(result.Error), StringComparison.Ordinal);
        Assert.EndsWith(".", TestTexts.En(result.Error), StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\?\", TestTexts.En(result.Error), StringComparison.Ordinal);
        Assert.Empty(_chunksRead);
    }

    [Fact(Skip = "Only Windows refuses a read in a range another handle has locked; Linux locks are advisory.", SkipUnless = nameof(IsWindows))]
    [SupportedOSPlatform("windows")]
    public async Task Hash_of_a_file_that_changed_and_then_failed_to_read_is_changed_while_reading()
    {
        //Arrange
        // A file still being written can fail the read too: then the change is the real story.
        var movie = _folder.WriteFile(TenBytes, "media", "movie.mkv");
        using var holder = new FileStream(movie, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var inventory = Inventory(SmallChunkBytes, OnFirstChunk(() =>
        {
            holder.Seek(0, SeekOrigin.End);
            holder.Write("abcde"u8);
            holder.Flush();
            holder.Lock(SmallChunkBytes, 1 << 10);
        }));

        //Act
        var result = await HashAsync(inventory, movie, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.ChangedWhileReading, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(ChangedWhileHashing(movie), TestTexts.En(result.Error));
        long[] chunks = [SmallChunkBytes];
        Assert.Equal(chunks, _chunksRead);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The real inventory reading <paramref name="chunkSize"/> bytes at a time, recording each chunk in
    /// <see cref="_chunksRead"/> and then calling <paramref name="afterChunk"/>. The hash never runs ffprobe.
    /// </summary>
    private LocalFileInventory Inventory(int chunkSize = SmallChunkBytes, Action<long>? afterChunk = null) =>
        new(new FfprobeProcess())
        {
            HashChunkSize = chunkSize,
            AfterHashChunk = read =>
            {
                _chunksRead.Add(read);
                afterChunk?.Invoke(read);
            },
        };

    /// <summary>A hook that runs <paramref name="action"/> once, after the first chunk.</summary>
    private static Action<long> OnFirstChunk(Action action)
    {
        var done = false;
        return _ =>
        {
            if (done)
                return;
            done = true;
            action();
        };
    }

    private static Task<FileInventoryResult<FileContentHash>> HashAsync(LocalFileInventory inventory, string path, params string[] roots) =>
        inventory.HashAsync(path, roots, TestContext.Current.CancellationToken);

    /// <summary>What sha256sum prints for the file's bytes as they are now.</summary>
    private static string Sha256Of(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string ChangedWhileHashing(string path) =>
        $"“{path}” changed while the app was reading it for its hash, so the hash can't be said to belong to either version.";

    private static bool CanStillRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
