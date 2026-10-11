using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Core.Tests.Conventions;

/// <summary>
/// MAM-26, ADR 0030, ADR 0033 — the write contract's own name guard: the app may rename a replaced copy into the
/// quarantine and back, and delete what sits there, and nothing else. So every member of <see cref="IFileQuarantine"/>
/// named for moving, restoring, deleting, removing, renaming or unlinking must name the quarantine too, and no member
/// may be named for copying, overwriting, replacing, writing, truncating, changing a mode or an owner, permissions or
/// links. A member that needs such a name is a decision for an ADR, not a quiet addition.
/// </summary>
public sealed partial class FileQuarantineNamesTests
{
    /// <summary>The changes the contract never makes, matched as whole words of a name.</summary>
    private static readonly string[] ForbiddenWords =
    [
        "copy", "copies", "overwrite", "overwrites", "replace", "replaces", "write", "writes", "truncate", "truncates",
        "chmod", "chown", "owner", "owners", "permission", "permissions", "link", "links",
    ];

    /// <summary>The changes it makes, each of which must name the quarantine in the same member's name.</summary>
    private static readonly string[] QuarantineOnlyWords =
    [
        "move", "moves", "restore", "restores", "delete", "deletes", "remove", "removes", "rename", "renames",
        "unlink", "unlinks",
    ];

    /// <summary>
    /// The verbs of a member that only reads: such a member may name what it reads about, as
    /// <see cref="IFileQuarantine.DescribeRenamePlacesAsync"/> names the places a rename can work.
    /// </summary>
    private static readonly string[] ReadingVerbs = ["describe", "list", "read", "find", "count"];

    [Fact]
    public void No_file_quarantine_member_is_named_for_a_change_it_never_makes()
    {
        //Arrange
        var members = MemberNames();

        //Act
        var offenders = members.Where(NamesAForbiddenChange).ToList();

        //Assert
        Assert.True(offenders.Count == 0, $"IFileQuarantine members named for a forbidden change: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Every_file_quarantine_member_that_moves_restores_or_deletes_names_the_quarantine()
    {
        //Arrange
        var members = MemberNames();

        //Act
        var offenders = members.Where(ChangesOutsideTheQuarantine).ToList();

        //Assert
        Assert.True(offenders.Count == 0, $"IFileQuarantine members that change a name without naming the quarantine: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_scan_covers_every_member_of_the_file_quarantine_contract()
    {
        //Arrange
        // Guard the guard: the scan sees today's members, so a new one is scanned too.
        string[] known =
        [
            nameof(IFileQuarantine.Capabilities), nameof(IFileQuarantine.ListFolderFilesAsync), nameof(IFileQuarantine.ListFolderNamesAsync),
            nameof(IFileQuarantine.MoveIntoQuarantineAsync), nameof(IFileQuarantine.RestoreFromQuarantineAsync),
            nameof(IFileQuarantine.DeleteFromQuarantineAsync), nameof(IFileQuarantine.DescribeRenamePlacesAsync),
        ];

        //Act
        var members = MemberNames();

        //Assert
        Assert.Subset(members.ToHashSet(StringComparer.Ordinal), known.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("CopyIntoQuarantineAsync", true)]
    [InlineData("OverwriteAsync", true)]
    [InlineData("ReplaceQuarantinedAsync", true)]
    [InlineData("WriteAsync", true)]
    [InlineData("TruncateAsync", true)]
    [InlineData("ChmodAsync", true)]
    [InlineData("SetOwnerAsync", true)]
    [InlineData("GrantPermissionsAsync", true)]
    [InlineData("LinkIntoQuarantineAsync", true)]
    [InlineData("MoveIntoQuarantineAsync", false)]
    [InlineData("DescribeRenamePlacesAsync", false)]
    [InlineData("ListFolderFilesAsync", false)]
    [InlineData("ListFolderNamesAsync", false)]
    [InlineData("Capabilities", false)]
    public void The_forbidden_change_detector_flags_a_forbidden_word_and_spares_the_contracts_own(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = NamesAForbiddenChange(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    [Theory]
    [InlineData("MoveAsync", true)]
    [InlineData("DeleteFileAsync", true)]
    [InlineData("RemoveAsync", true)]
    [InlineData("RenameAsync", true)]
    [InlineData("RestoreAsync", true)]
    [InlineData("unlinkFile", true)]
    [InlineData("TryDeleteAsync", true)]
    [InlineData("MoveIntoQuarantineAsync", false)]
    [InlineData("RestoreFromQuarantineAsync", false)]
    [InlineData("DeleteFromQuarantineAsync", false)]
    [InlineData("DeleteQuarantinedAsync", false)]
    [InlineData("DescribeRenamePlacesAsync", false)]
    [InlineData("ListFolderFilesAsync", false)]
    public void The_quarantine_detector_flags_a_change_that_doesnt_name_the_quarantine(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = ChangesOutsideTheQuarantine(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the scan ─────────────────────────────────────────────────────────────

    /// <summary>Every member name of the contract and the interfaces it extends — methods, properties and
    /// events — but not the accessors the compiler makes for a property.</summary>
    private static List<string> MemberNames() =>
    [
        .. new[] { typeof(IFileQuarantine) }
            .Concat(typeof(IFileQuarantine).GetInterfaces())
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name),
    ];

    private static bool NamesAForbiddenChange(string name) =>
        WordsOf(name).Any(word => ForbiddenWords.Contains(word, StringComparer.Ordinal));

    private static bool ChangesOutsideTheQuarantine(string name)
    {
        var words = WordsOf(name);
        return words.Count > 0
            && !ReadingVerbs.Contains(words[0], StringComparer.Ordinal)
            && words.Any(word => QuarantineOnlyWords.Contains(word, StringComparer.Ordinal))
            && !words.Any(word => word is "quarantine" or "quarantined");
    }

    private static List<string> WordsOf(string name) => [.. Words().Matches(name).Select(match => match.Value.ToLowerInvariant())];

    /// <summary>The words of a PascalCase or camelCase name, as <see cref="CoreIsVendorNeutralTests"/> splits
    /// them.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
