using MediaAcquisitionManager.Infrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-20 — Linux's mount table read for the file system type of a device (ADR 0028): the device number decoded the way
// glibc's makedev made it, the type found after the " - " that ends the optional fields, a device mounted twice known
// only when its lines agree, and nothing guessed from a line that can't be read. MAM-26 — and for a mount's own
// options by its id (ADR 0033): the sixth field, never the file system's options after the type.
public sealed class MountTableTests
{
    // makedev(8, 1) = 2049: the first partition of the first SCSI disk.
    private const long Sda1 = 2049;

    // makedev(8, 17) = 2065: "8:17", which begins with "8:1".
    private const long Sdb1 = 2065;

    // makedev(0, 50) = 50: an anonymous device, as overlay and btrfs subvolumes have.
    private const long Anonymous50 = 50;

    private const string Table = """
        22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw,errors=remount-ro
        23 22 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:12 - proc proc rw
        24 22 8:17 / /media/movies rw,relatime shared:9 - xfs /dev/sdb1 rw,attr2,inode64
        """;

    [Theory]
    [InlineData(Sda1, "ext4")]
    [InlineData(Sdb1, "xfs")]
    [InlineData(21L, "proc")]
    public void FileSystemTypeOf_finds_the_line_of_the_device_by_its_major_and_minor(long deviceId, string expected)
    {
        //Arrange

        //Act
        var type = MountTable.FileSystemTypeOf(Table, deviceId);

        //Assert
        Assert.Equal(expected, type);
    }

    [Theory]
    // makedev(259, 300) = 1114924: an NVMe partition whose minor is above 255, so its high bits sit above the major's.
    [InlineData(1_114_924L, "259:300")]
    // makedev(5000, 70000) = 17592472537200: a major above 4095, whose high bits sit above bit 32.
    [InlineData(17_592_472_537_200L, "5000:70000")]
    // makedev(0x80000000, 3): the top bit set, so the stored signed number is negative; decoded from its unsigned bits.
    [InlineData(-9_223_372_036_854_775_805L, "2147483648:3")]
    public void FileSystemTypeOf_decodes_a_large_device_number_the_way_glibc_encodes_it(long deviceId, string majorMinor)
    {
        //Arrange
        var mountInfo = $"40 22 {majorMinor} / /media/fast rw,relatime shared:20 - btrfs /dev/nvme0n1p1 rw,ssd";

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, deviceId);

        //Assert
        Assert.Equal("btrfs", type);
    }

    [Theory]
    // No optional field: the separator comes right after the options.
    [InlineData("30 22 8:1 / /data rw,relatime - ext4 /dev/sda1 rw")]
    // Several optional fields.
    [InlineData("30 22 8:1 / /data rw,relatime shared:5 master:2 propagate_from:1 - ext4 /dev/sda1 rw")]
    // An unbindable mount's lone optional field.
    [InlineData("30 22 8:1 / /data rw,relatime unbindable - ext4 /dev/sda1 rw")]
    public void FileSystemTypeOf_finds_the_type_after_the_separator_whatever_the_optional_fields(string mountInfo)
    {
        //Arrange

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, Sda1);

        //Assert
        Assert.Equal("ext4", type);
    }

    [Fact]
    public void FileSystemTypeOf_a_device_bind_mounted_where_its_lines_agree_is_their_type()
    {
        //Arrange
        // A container's volumes: the same disk mounted at the root and bound into two folders.
        const string mountInfo = """
            22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw
            31 22 8:1 /srv/media /media rw,relatime shared:1 - ext4 /dev/sda1 rw
            32 22 8:1 /srv/downloads /downloads rw,relatime shared:1 - ext4 /dev/sda1 rw
            """;

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, Sda1);

        //Assert
        Assert.Equal("ext4", type);
    }

    [Fact]
    public void FileSystemTypeOf_a_device_whose_lines_disagree_is_unknown()
    {
        //Arrange
        const string mountInfo = """
            22 1 0:50 / / rw,relatime master:1 - overlay overlay rw,lowerdir=/l,upperdir=/u,workdir=/w
            31 22 0:50 / /media rw,relatime shared:1 - ext4 /dev/sda1 rw
            """;

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, Anonymous50);

        //Assert
        Assert.Null(type);
    }

    [Theory]
    // 8:1 is asked; 8:17 and 18:1 only look like it.
    [InlineData("24 22 8:17 / /media rw,relatime shared:9 - xfs /dev/sdb1 rw\n25 22 18:1 / /mnt rw - ext4 /dev/x rw")]
    [InlineData("")]
    public void FileSystemTypeOf_a_device_no_line_names_is_unknown(string mountInfo)
    {
        //Arrange

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, Sda1);

        //Assert
        Assert.Null(type);
    }

    [Fact]
    public void FileSystemTypeOf_reads_a_line_whose_mount_point_has_an_escaped_space()
    {
        //Arrange
        // The kernel writes a space in a path as \040, so the line still splits into its fields at spaces.
        const string mountInfo = @"40 22 8:1 /My\040Movies /media/My\040Movies rw,relatime shared:9 - ext4 /dev/sda1 rw";

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, Sda1);

        //Assert
        Assert.Equal("ext4", type);
    }

    [Fact]
    public void FileSystemTypeOf_skips_a_malformed_line_and_reads_the_others()
    {
        //Arrange
        // Too few fields; no separator; a separator with nothing after it; then the device's good line.
        const string mountInfo = """
            garbage
            40 22 8:1
            41 22 8:1 / /broken rw,relatime shared:1 ext4 /dev/sda1 rw
            42 22 8:1 / /cut rw,relatime shared:1 -
            43 22 8:1 / /media rw,relatime shared:1 - ext4 /dev/sda1 rw

            """;

        //Act
        var type = MountTable.FileSystemTypeOf(mountInfo, Sda1);

        //Assert
        Assert.Equal("ext4", type);
    }

    // ── MAM-26: a mount's own options ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(22UL, new[] { "rw", "relatime" })]
    [InlineData(24UL, new[] { "rw", "relatime" })]
    [InlineData(36UL, new[] { "ro", "noatime" })]
    public void MountOptionsOf_finds_the_line_of_the_mount_by_its_id_and_gives_its_own_options(ulong mountId, string[] expected)
    {
        //Arrange
        const string mountInfo = """
            22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw,errors=remount-ro
            24 22 8:17 / /media/movies rw,relatime shared:9 - xfs /dev/sdb1 rw,attr2,inode64
            36 22 8:1 /srv/media /media ro,noatime shared:2 - ext4 /dev/sda1 rw,errors=remount-ro
            """;

        //Act
        var options = MountTable.MountOptionsOf(mountInfo, mountId);

        //Assert
        Assert.Equal(expected, options);
    }

    [Fact]
    public void MountOptionsOf_a_read_only_bind_of_a_writable_disk_says_ro_whatever_the_file_system_says()
    {
        //Arrange
        // A :ro bind in a container: the mount's own options say ro, the super-options after the type say rw. Only the
        // first stops a rename.
        const string mountInfo = "512 498 0:73 /media /media ro,relatime master:1 - 9p drvfs rw,dirsync,aname=drvfs";

        //Act
        var options = MountTable.MountOptionsOf(mountInfo, 512);

        //Assert
        Assert.Contains("ro", options!);
        Assert.DoesNotContain("rw", options!);
    }

    [Theory]
    // 3 is asked; 36 and 13 only look like it, and 3 as a parent's id is no mount's own.
    [InlineData("36 22 8:1 / /media ro,noatime shared:2 - ext4 /dev/sda1 rw\n13 3 8:1 / /x rw - ext4 /dev/sda1 rw")]
    [InlineData("")]
    // Too few fields to have options.
    [InlineData("3 1 8:1 / /")]
    public void MountOptionsOf_a_mount_no_line_names_is_unknown(string mountInfo)
    {
        //Arrange

        //Act
        var options = MountTable.MountOptionsOf(mountInfo, 3);

        //Assert
        Assert.Null(options);
    }
}
