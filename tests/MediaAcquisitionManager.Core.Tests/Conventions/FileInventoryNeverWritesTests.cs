using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Core.Tests.Conventions;

/// <summary>
/// MAM-26, ADR 0030, ADR 0033 — the app's look at its files stays read-only by construction: every write to the media
/// folders goes through <see cref="IFileQuarantine"/>, a contract of its own with a name guard of its own
/// (<see cref="FileQuarantineNamesTests"/>). So no member of <see cref="IFileInventory"/> — nor of an interface it
/// extends — may be named for writing, moving, renaming, deleting, removing, creating, copying, setting, relocating,
/// unlinking, truncating or restoring anything. A member that needs such a name belongs on the quarantine, or is a
/// decision for an ADR.
/// </summary>
public sealed partial class FileInventoryNeverWritesTests
{
    /// <summary>The verbs of changing a file or a folder, matched as whole words of a name.</summary>
    private static readonly string[] ForbiddenWords =
    [
        "write", "writes", "move", "moves", "rename", "renames", "delete", "deletes", "remove", "removes",
        "create", "creates", "copy", "copies", "set", "sets", "relocate", "relocates", "unlink", "unlinks",
        "truncate", "truncates", "restore", "restores",
    ];

    [Fact]
    public void No_file_inventory_member_is_named_for_a_write()
    {
        //Arrange
        var members = MemberNames();

        //Act
        var offenders = members.Where(NamesAWrite).ToList();

        //Assert
        Assert.True(offenders.Count == 0, $"IFileInventory members that write: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_scan_covers_every_member_of_the_file_inventory_contract()
    {
        //Arrange
        // Guard the guard: the scan sees today's members, so a new one is scanned too.
        string[] known =
        [
            nameof(IFileInventory.Capabilities), nameof(IFileInventory.DescribeAsync), nameof(IFileInventory.InspectMediaAsync),
            nameof(IFileInventory.HashAsync), nameof(IFileInventory.HashPiecesAsync), nameof(IFileInventory.ListMediaFilesAsync),
            nameof(IFileInventory.DescribeFileSystemsAsync),
        ];

        //Act
        var members = MemberNames();

        //Assert
        Assert.Subset(members.ToHashSet(StringComparer.Ordinal), known.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("WriteAsync", true)]
    [InlineData("MoveFileAsync", true)]
    [InlineData("RenameAsync", true)]
    [InlineData("DeleteAsync", true)]
    [InlineData("RemoveFolderAsync", true)]
    [InlineData("CreateFolderAsync", true)]
    [InlineData("CopyToAsync", true)]
    [InlineData("SetModifiedTimeAsync", true)]
    [InlineData("RelocateAsync", true)]
    [InlineData("unlinkFile", true)]
    [InlineData("TruncateAsync", true)]
    [InlineData("RestoreAsync", true)]
    [InlineData("DescribeAsync", false)]
    [InlineData("InspectMediaAsync", false)]
    [InlineData("HashPiecesAsync", false)]
    [InlineData("ListMediaFilesAsync", false)]
    [InlineData("DescribeFileSystemsAsync", false)]
    [InlineData("ReadSettingsAsync", false)]
    [InlineData("Capabilities", false)]
    public void The_detector_flags_a_writing_verb_and_spares_an_ordinary_name(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = NamesAWrite(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the scan ─────────────────────────────────────────────────────────────

    /// <summary>Every member name of the contract and the interfaces it extends — methods, properties and
    /// events — but not the accessors the compiler makes for a property.</summary>
    private static List<string> MemberNames() =>
    [
        .. new[] { typeof(IFileInventory) }
            .Concat(typeof(IFileInventory).GetInterfaces())
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name),
    ];

    private static bool NamesAWrite(string name) =>
        Words().Matches(name).Any(match => ForbiddenWords.Contains(match.Value.ToLowerInvariant(), StringComparer.Ordinal));

    /// <summary>The words of a PascalCase or camelCase name, as <see cref="CoreIsVendorNeutralTests"/> splits
    /// them.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
