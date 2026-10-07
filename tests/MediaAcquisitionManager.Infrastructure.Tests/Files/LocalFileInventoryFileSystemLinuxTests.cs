using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-20 — the file system type of a device through the real inventory and the real mount table (ADR 0028): the device a
// file is on, as the inventory describes it, is found in /proc/self/mountinfo; a device no line names, or a table that
// can't be read, is unknown — no estimate rather than a guess. The parsing itself is in MountTableTests.
// Linux only: this is where the app runs, and where CI runs the tests.
public sealed class LocalFileInventoryFileSystemLinuxTests : IDisposable
{
    private const string LinuxOnly = "The mount table is Linux's; CI runs these tests there.";

    private readonly TemporaryFolder _folder = new("mam-file-system-linux-");
    private readonly string _media;

    public LocalFileInventoryFileSystemLinuxTests() => _media = _folder.CreateFolder("media");

    /// <summary>For <c>SkipUnless</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose() => _folder.Dispose();

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeFileSystems_of_the_device_a_file_is_on_names_its_type()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = _folder.WriteFile("the movie", "media", "movie.mkv");
        var inventory = new LocalFileInventory(new FfprobeProcess());
        var deviceId = (await inventory.DescribeAsync(path, [_media], cancellationToken)).Value!.DeviceId!.Value;

        //Act
        var described = await inventory.DescribeFileSystemsAsync([deviceId], cancellationToken);

        //Assert
        // Whatever the runner's temporary folder is on — ext4, overlay in a container, tmpfs — the table names it.
        var fileSystem = Assert.Single(described);
        Assert.Equal(deviceId, fileSystem.DeviceId);
        Assert.False(string.IsNullOrWhiteSpace(fileSystem.Type));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeFileSystems_of_a_device_no_mount_names_is_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // makedev(4000, 4000000): numbers no disk, partition or anonymous device is given.
        const long unmounted = 16_385_024_000L;
        var inventory = new LocalFileInventory(new FfprobeProcess());

        //Act
        var described = await inventory.DescribeFileSystemsAsync([unmounted], cancellationToken);

        //Assert
        var fileSystem = Assert.Single(described);
        Assert.Equal(unmounted, fileSystem.DeviceId);
        Assert.Null(fileSystem.Type);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeFileSystems_names_every_device_asked_in_their_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // makedev(8, 1) = 2049 and makedev(8, 17) = 2065.
        var mountInfo = _folder.WriteFile(
            "22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw\n24 22 8:17 / /media rw,relatime shared:9 - btrfs /dev/sdb1 rw\n",
            "mountinfo");
        var inventory = new LocalFileInventory(new FfprobeProcess()) { MountInfoPath = mountInfo };

        //Act
        var described = await inventory.DescribeFileSystemsAsync([2065, 2049, 7], cancellationToken);

        //Assert
        Assert.Equal([new DeviceFileSystem(2065, "btrfs"), new DeviceFileSystem(2049, "ext4"), new DeviceFileSystem(7, null)], described);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task DescribeFileSystems_where_the_mount_table_cant_be_read_is_unknown_for_every_device()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var inventory = new LocalFileInventory(new FfprobeProcess()) { MountInfoPath = _folder.PathOf("no-such-mountinfo") };

        //Act
        var described = await inventory.DescribeFileSystemsAsync([2049, 2065], cancellationToken);

        //Assert
        Assert.Equal([new DeviceFileSystem(2049, null), new DeviceFileSystem(2065, null)], described);
    }
}
