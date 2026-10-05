using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-19 — the hash where only Linux can show it (ADR 0022, ADR 0009): a pipe named like media is refused without
// being opened, so a hash can't hang the run; and the bytes hashed are those of the file the reader opened, whatever
// its path leads to by the time they are read — a link to the app's data swapped in, or a new file moved over the
// name — with the facts it was opened with, which still hold. The rest of the hash is in LocalFileInventoryHashTests.
// Linux only: this is where the app runs, and where CI runs the tests.
public sealed class LocalFileInventoryHashLinuxTests : IDisposable
{
    private const string LinuxOnly = "The Linux reader runs on Linux only; CI runs these tests there.";

    private const string Movie = "the movie, read whole a few bytes at a time";

    private readonly TemporaryFolder _folder = new("mam-file-hash-linux-");
    private readonly string _media;

    public LocalFileInventoryHashLinuxTests() => _media = _folder.CreateFolder("media");

    /// <summary>For <c>SkipUnless</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux), Timeout = 10_000)]
    public async Task Hash_of_a_named_pipe_is_not_a_regular_file_and_is_never_opened()
    {
        //Arrange
        // Opened for reading, a pipe with no writer blocks until one comes: the hash would hang the run.
        var pipe = _folder.PathOf("media", "movie.mkv");
        CreateNamedPipe(pipe);
        var chunks = new List<long>();
        var inventory = new LocalFileInventory(new FfprobeProcess()) { AfterHashChunk = chunks.Add };

        //Act
        var result = await inventory.HashAsync(pipe, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.NotARegularFile, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"“{pipe}” is not a regular file but a device, a socket or a pipe.", result.Error);
        Assert.Empty(chunks);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Hash_reads_the_file_it_opened_after_its_path_was_swapped_for_a_link_leading_out()
    {
        //Arrange
        // After the first chunk, someone who may write in /media moves the movie aside and puts a link to the
        // app's data in its place.
        var path = _folder.WriteFile(Movie, "media", "movie.mkv");
        var outside = _folder.WriteFile("secret", "outside", "mam.db");
        var inventory = InventorySwappingAfterTheFirstChunk(() =>
        {
            File.Move(path, _folder.PathOf("media", "aside.mkv"));
            File.CreateSymbolicLink(path, outside);
        });
        var before = (await inventory.DescribeAsync(path, [_media], TestContext.Current.CancellationToken)).Value!;

        //Act
        var result = await inventory.HashAsync(path, [_media], TestContext.Current.CancellationToken);

        //Assert
        // Not one byte of the app's data: the rest was read from the descriptor opened after the checks.
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(Sha256Of(Movie), result.Value!.Sha256);
        Assert.Equal(before, result.Value.File);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Hash_reads_the_file_it_opened_after_a_new_file_was_moved_over_its_name()
    {
        //Arrange
        // How a download client or an editor replaces a file: a new one written beside it, then moved over the name.
        var path = _folder.WriteFile(Movie, "media", "movie.mkv");
        var inventory = InventorySwappingAfterTheFirstChunk(() =>
        {
            var replacement = _folder.WriteFile("another movie", "media", "movie.mkv.part");
            File.Move(replacement, path, overwrite: true);
        });
        var before = (await inventory.DescribeAsync(path, [_media], TestContext.Current.CancellationToken)).Value!;

        //Act
        var result = await inventory.HashAsync(path, [_media], TestContext.Current.CancellationToken);

        //Assert
        // The opened file lost its only name meanwhile, yet its identity, size and modification time held, so the
        // hash belongs to the key it was opened with: the link count is no part of a version.
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(Sha256Of(Movie), result.Value!.Sha256);
        Assert.Equal(before, result.Value.File);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The real inventory reading 4 bytes at a time, running <paramref name="swap"/> once, after the first
    /// chunk.</summary>
    private static LocalFileInventory InventorySwappingAfterTheFirstChunk(Action swap)
    {
        var swapped = false;
        return new LocalFileInventory(new FfprobeProcess())
        {
            HashChunkSize = 4,
            AfterHashChunk = _ =>
            {
                if (swapped)
                    return;
                swapped = true;
                swap();
            },
        };
    }

    private static string Sha256Of(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static void CreateNamedPipe(string path)
    {
        if (MakeFifo(path, Convert.ToUInt32("644", 8)) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
