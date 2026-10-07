using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-20 — whether removing a byte-identical copy frees its room, by the file system it is on (ADR 0028): an estimate
// only where the file system is known to give every file its own blocks; one that can share them, or one not known,
// gives none (Vasyl, 2026-10-07).
public sealed class FileSystemSharingTests
{
    [Theory]
    [InlineData("ext2")]
    [InlineData("ext3")]
    [InlineData("ext4")]
    [InlineData("jfs")]
    [InlineData("reiserfs")]
    [InlineData("vfat")]
    [InlineData("exfat")]
    [InlineData("ntfs")]
    [InlineData("ntfs3")]
    public void GapOf_a_file_system_whose_files_never_share_blocks_is_no_gap(string fileSystemType)
    {
        //Arrange

        //Act
        var gap = FileSystemSharing.GapOf(fileSystemType);

        //Assert
        Assert.Null(gap);
    }

    [Theory]
    [InlineData("btrfs")]
    [InlineData("zfs")]
    [InlineData("xfs")]
    [InlineData("bcachefs")]
    [InlineData("apfs")]
    [InlineData("refs")]
    [InlineData("ocfs2")]
    [InlineData("nilfs2")]
    public void GapOf_a_file_system_that_can_share_blocks_is_a_sharing_file_system(string fileSystemType)
    {
        //Arrange

        //Act
        var gap = FileSystemSharing.GapOf(fileSystemType);

        //Assert
        Assert.Equal(RecoverableSpaceGap.SharingFileSystem, gap);
    }

    [Theory]
    [InlineData("EXT4", null)]
    [InlineData("  ext4  ", null)]
    [InlineData("\tNtfs3\n", null)]
    [InlineData("Btrfs", RecoverableSpaceGap.SharingFileSystem)]
    [InlineData(" ZFS ", RecoverableSpaceGap.SharingFileSystem)]
    public void GapOf_takes_a_type_in_any_letter_case_and_with_whitespace_around_it(string fileSystemType, RecoverableSpaceGap? expected)
    {
        //Arrange

        //Act
        var gap = FileSystemSharing.GapOf(fileSystemType);

        //Assert
        Assert.Equal(expected, gap);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // A network share's server, an overlay's or a FUSE file system's lower layer may share blocks without saying so.
    [InlineData("nfs")]
    [InlineData("nfs4")]
    [InlineData("cifs")]
    [InlineData("fuse.mergerfs")]
    [InlineData("overlay")]
    [InlineData("ext5")]
    public void GapOf_a_blank_or_any_other_type_is_an_unknown_file_system(string? fileSystemType)
    {
        //Arrange

        //Act
        var gap = FileSystemSharing.GapOf(fileSystemType);

        //Assert
        Assert.Equal(RecoverableSpaceGap.UnknownFileSystem, gap);
    }
}
