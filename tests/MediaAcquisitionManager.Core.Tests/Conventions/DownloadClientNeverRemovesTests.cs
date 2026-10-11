using System.Reflection;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Conventions;

/// <summary>
/// MAM-14 — cancelling a download job only stops following it; the torrent and its files stay in the client
/// (ADR 0014). The contract makes that hold for every client by having no way to do otherwise: no member of
/// <see cref="IDownloadClient"/> — nor of an interface it extends — may be named for removing, deleting,
/// stopping or pausing anything. A member that needs such a name is a decision for an ADR, not a quiet
/// addition. MAM-26 — nor for moving, relocating or renaming anything, or setting where it lies (ADR 0031: a
/// replacement never writes to a download client, and a torrent's files stay where it put them).
/// </summary>
public sealed partial class DownloadClientNeverRemovesTests
{
    /// <summary>The verbs of taking a job or its files away, or holding it, matched as whole words of a name. MAM-26 —
    /// and of putting them elsewhere (ADR 0031).</summary>
    private static readonly string[] ForbiddenWords =
    [
        "remove", "delete", "erase", "purge", "trash", "discard", "destroy", "drop", "clear", "wipe",
        "stop", "pause", "halt", "suspend", "abort", "kill", "cancel",
        "move", "relocate", "rename",
    ];

    /// <summary>
    /// MAM-26 — phrases whose words are harmless alone, matched as consecutive words of a name: setting a job's location
    /// (ADR 0031 names it, since a <c>SetLocationAsync</c> passed the words alone). Our addition beyond the ADR: setting
    /// its save path or its download path, qBittorrent's other ways to put a torrent's files elsewhere.
    /// </summary>
    private static readonly string[][] ForbiddenPhrases =
    [
        ["set", "location"],
        ["set", "save", "path"],
        ["set", "download", "path"],
    ];

    [Fact]
    public void No_download_client_member_is_named_for_removing_deleting_stopping_or_pausing()
    {
        //Arrange
        var members = MemberNames();

        //Act
        var offenders = members.Where(NamesARemoval).ToList();

        //Assert
        Assert.True(
            offenders.Count == 0,
            $"IDownloadClient members that remove, delete, stop, pause, move, relocate, rename or set a location: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_scan_covers_every_member_of_the_download_client_contract()
    {
        //Arrange
        // Guard the guard: the scan sees today's members, so a new one is scanned too.
        string[] known =
        [
            nameof(IDownloadClient.Capabilities), nameof(IDownloadClient.ListDestinationsAsync), nameof(IDownloadClient.SubmitAsync),
            nameof(IDownloadClient.FindJobsAsync), nameof(IDownloadClient.ListJobFilesAsync), nameof(IDownloadClient.ListDestinationJobsAsync),
            // MAM-24 — the whole listing with every job's files (ADR 0031): a read, scanned like the rest.
            nameof(IDownloadClient.ListJobsWithFilesAsync),
        ];

        //Act
        var members = MemberNames();

        //Assert
        Assert.Subset(members.ToHashSet(StringComparer.Ordinal), known.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("DeleteJobAsync", true)]
    [InlineData("RemoveTorrentsAsync", true)]
    [InlineData("StopAsync", true)]
    [InlineData("PauseJobAsync", true)]
    [InlineData("deleteFiles", true)]
    [InlineData("CancelJobAsync", true)]
    [InlineData("MoveJobAsync", true)]
    [InlineData("RelocateTorrentAsync", true)]
    [InlineData("RenameFileAsync", true)]
    [InlineData("renameFolder", true)]
    [InlineData("SetLocationAsync", true)]
    [InlineData("setLocation", true)]
    [InlineData("SetSavePathAsync", true)]
    [InlineData("setDownloadPath", true)]
    [InlineData("LocationOfAsync", false)]
    [InlineData("SetCategoryAsync", false)]
    [InlineData("ReadSavePathAsync", false)]
    [InlineData("SettingsLocation", false)]
    [InlineData("ListDestinationsAsync", false)]
    [InlineData("ListDestinationJobsAsync", false)]
    [InlineData("ListJobsWithFilesAsync", false)]
    [InlineData("FindJobsAsync", false)]
    [InlineData("SubmitAsync", false)]
    [InlineData("Capabilities", false)]
    public void The_detector_flags_a_removing_verb_and_spares_an_ordinary_name(string name, bool expected)
    {
        //Arrange

        //Act
        var flagged = NamesARemoval(name);

        //Assert
        Assert.Equal(expected, flagged);
    }

    // ── the scan ─────────────────────────────────────────────────────────────

    /// <summary>Every member name of the contract and the interfaces it extends — methods, properties and
    /// events — but not the accessors the compiler makes for a property.</summary>
    private static List<string> MemberNames() =>
    [
        .. new[] { typeof(IDownloadClient) }
            .Concat(typeof(IDownloadClient).GetInterfaces())
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name),
    ];

    private static bool NamesARemoval(string name)
    {
        string[] words = [.. Words().Matches(name).Select(match => match.Value.ToLowerInvariant())];
        return words.Any(word => ForbiddenWords.Contains(word, StringComparer.Ordinal))
            || ForbiddenPhrases.Any(phrase => Enumerable.Range(0, Math.Max(0, words.Length - phrase.Length + 1))
                .Any(start => words.AsSpan(start, phrase.Length).SequenceEqual(phrase)));
    }

    /// <summary>The words of a PascalCase or camelCase name, as <see cref="CoreIsVendorNeutralTests"/> splits
    /// them.</summary>
    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")]
    private static partial Regex Words();
}
