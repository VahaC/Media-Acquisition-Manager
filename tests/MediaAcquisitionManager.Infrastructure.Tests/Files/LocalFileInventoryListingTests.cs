using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Listed = (MediaAcquisitionManager.Core.Abstractions.Files.FileListingStatus Status, string AppPath, string? ResolvedPath, string? Error);

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-18 — the walk of the allowed roots for media file names, over real folders in a temp folder (ADR 0021): media
// by extension, every folder walked in ordinal name order, links inside the roots followed under their own names and
// refused when they lead out, loops ended, a root that is away or empty answered as unlisted rather than as empty —
// reached through a link in another root too — a folder's names taken from the folder that was opened, the walk
// bounded, and (MAM-26) every quarantine passed over in silence. A link to a folder is a junction on Windows, which needs no privilege; a symbolic link to a
// file is skipped on a Windows account that may not create one and runs on Linux in CI. What only Linux can show —
// a folder swapped for a link, names that aren't UTF-8, a folder the app may not list — is in
// LocalFileInventoryListingLinuxTests.
public sealed class LocalFileInventoryListingTests : IDisposable
{
    private const string LimitReachedError =
        "The media folders hold more than {0} media files or {1} folders, or links inside them lead to the same folders many times over, so the scan stopped.";

    private const string PathTooLongEnding = "” is longer than 4096 characters, so the app skips it and everything in it.";

    private readonly TemporaryFolder _folder = new("mam-file-listing-");

    // The walk never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());

    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryListingTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task List_gives_the_media_files_by_extension_in_any_letter_case()
    {
        //Arrange
        _folder.WriteFile("1", "media", "a.mkv");
        _folder.WriteFile("2", "media", "B.MP4");
        _folder.WriteFile("3", "media", "c.Iso");
        _folder.WriteFile("4", "media", "notes.txt");
        _folder.WriteFile("5", "media", "poster.jpg");
        _folder.WriteFile("6", "media", "film.mkv.part");
        _folder.WriteFile("7", "media", "movie.nfo");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal([Media(_media, "B.MP4"), Media(_media, "a.mkv"), Media(_media, "c.Iso")], entries);
    }

    [Fact]
    public async Task List_walks_nested_and_hidden_folders_and_gives_hidden_files()
    {
        //Arrange
        _folder.WriteFile("1", "media", "Movies", "Film (2020)", "film.mkv");
        _folder.WriteFile("2", "media", "Shows", "Show", "Season 01", "S01E01.mkv");
        _folder.WriteFile("3", "media", ".hidden", "secret.mkv");
        _folder.WriteFile("4", "media", ".dot.mkv");
        // Hidden and system by attribute, as Windows has it (Linux has no such attribute and ignores it).
        _folder.WriteFile("5", "media", "attributes", "x.mkv");
        File.SetAttributes(_folder.PathOf("media", "attributes"), FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                Media(_media, ".dot.mkv"),
                Media(_media, ".hidden", "secret.mkv"),
                Media(_media, "Movies", "Film (2020)", "film.mkv"),
                Media(_media, "Shows", "Show", "Season 01", "S01E01.mkv"),
                Media(_media, "attributes", "x.mkv"),
            ],
            entries);
    }

    [Fact]
    public async Task List_gives_each_folders_entries_in_ordinal_name_order_depth_first()
    {
        //Arrange
        // Ordinal, never the culture's: capitals before the underscore, the underscore before small letters, and a
        // folder's names where the folder's own name falls.
        _folder.WriteFile("1", "media", "gamma.mkv");
        _folder.WriteFile("2", "media", "beta", "one.mkv");
        _folder.WriteFile("3", "media", "beta", "Two.mkv");
        _folder.WriteFile("4", "media", "alpha.mkv");
        _folder.WriteFile("5", "media", "_under.mkv");
        _folder.WriteFile("6", "media", "Zeta.mkv");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                Media(_media, "Zeta.mkv"),
                Media(_media, "_under.mkv"),
                Media(_media, "alpha.mkv"),
                Media(_media, "beta", "Two.mkv"),
                Media(_media, "beta", "one.mkv"),
                Media(_media, "gamma.mkv"),
            ],
            entries);
    }

    [Fact]
    public async Task List_gives_nothing_for_other_files_and_an_empty_folder_below_the_root()
    {
        //Arrange
        // Only a root that lists nothing is a problem: an empty folder below one is just empty.
        _folder.WriteFile("1", "media", "readme.txt");
        _folder.WriteFile("2", "media", "Film", "poster.jpg");
        _folder.CreateFolder("media", "empty");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task List_follows_a_folder_link_inside_the_root_under_the_links_own_name()
    {
        //Arrange
        _folder.WriteFile("1", "media", "real", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "linked"), Path.Combine(_media, "real"));

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                MediaFile(Path.Combine(_media, "linked", "movie.mkv"), Path.Combine(_media, "real", "movie.mkv")),
                Media(_media, "real", "movie.mkv"),
            ],
            entries);
    }

    [Fact]
    public async Task List_gives_every_name_of_a_folder_two_links_lead_to()
    {
        //Arrange
        // A diamond: two ways to one folder are not a loop, and each is a name of its files.
        _folder.WriteFile("1", "media", "shared", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "left"), Path.Combine(_media, "shared"));
        FileLinks.CreateFolderLink(Path.Combine(_media, "right"), Path.Combine(_media, "shared"));
        var file = Path.Combine(_media, "shared", "movie.mkv");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                MediaFile(Path.Combine(_media, "left", "movie.mkv"), file),
                MediaFile(Path.Combine(_media, "right", "movie.mkv"), file),
                MediaFile(file, file),
            ],
            entries);
    }

    [Fact]
    public async Task List_refuses_a_folder_link_leading_out_and_lists_nothing_behind_it()
    {
        //Arrange
        _folder.WriteFile("secret", "outside", "secret.mkv");
        _folder.WriteFile("1", "media", "movie.mkv");
        var link = Path.Combine(_media, "escape");
        FileLinks.CreateFolderLink(link, _outside);

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                Problem(FileListingStatus.Skipped, link, $"“{link}” leads through a link to “{_outside}”, outside the media folders the app may read."),
                Media(_media, "movie.mkv"),
            ],
            entries);
    }

    [Fact]
    public async Task List_follows_a_link_into_another_allowed_root()
    {
        //Arrange
        // /media/downloads -> /downloads, both allowed: two mounts, one library.
        var downloads = _folder.CreateFolder("downloads");
        _folder.WriteFile("1", "downloads", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "downloads"), downloads);
        var file = Path.Combine(downloads, "movie.mkv");

        //Act
        var entries = await ListAsync(_media, downloads);

        //Assert
        Assert.Equal([MediaFile(Path.Combine(_media, "downloads", "movie.mkv"), file), MediaFile(file, file)], entries);
    }

    [Fact]
    public async Task List_answers_an_empty_root_reached_through_a_link_in_another_root_as_unlisted_both_times()
    {
        //Arrange
        // /media/downloads -> /downloads, both allowed, and the /downloads disk is away: reached through the link it is
        // still that root, so the names found under /media/downloads before are kept as well as those under /downloads.
        var downloads = _folder.CreateFolder("downloads");
        var link = Path.Combine(_media, "downloads");
        FileLinks.CreateFolderLink(link, downloads);

        //Act
        var entries = await ListAsync(_media, downloads);

        //Assert
        Assert.Equal(
            [
                Problem(FileListingStatus.Unlisted, link, $"“{Canonical(link)}” is empty; if it is a mount, it may be away, so the files found in it before are kept."),
                Problem(FileListingStatus.Unlisted, downloads, $"“{Canonical(downloads)}” is empty; if it is a mount, it may be away, so the files found in it before are kept."),
            ],
            entries);
    }

    [Fact]
    public async Task List_skips_a_link_to_the_folder_it_is_in_or_one_above_and_walks_on()
    {
        //Arrange
        var folder = _folder.CreateFolder("media", "Film");
        _folder.WriteFile("1", "media", "Film", "movie.mkv");
        var here = Path.Combine(folder, "here");
        var up = Path.Combine(folder, "up");
        FileLinks.CreateFolderLink(here, folder);
        FileLinks.CreateFolderLink(up, _media);

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                Problem(FileListingStatus.Skipped, here, $"“{here}” leads back to a folder above it, so the app doesn't walk it again."),
                Media(_media, "Film", "movie.mkv"),
                Problem(FileListingStatus.Skipped, up, $"“{up}” leads back to a folder above it, so the app doesn't walk it again."),
            ],
            entries);
    }

    [Fact]
    public async Task List_refuses_links_that_lead_to_each_other()
    {
        //Arrange
        var first = Path.Combine(_media, "loop-a");
        var second = Path.Combine(_media, "loop-b");
        FileLinks.CreateFolderLink(first, second);
        FileLinks.CreateFolderLink(second, first);

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                Problem(FileListingStatus.Skipped, first, $"“{first}” leads through more than 40 links, likely a loop, so the app doesn't follow them."),
                Problem(FileListingStatus.Skipped, second, $"“{second}” leads through more than 40 links, likely a loop, so the app doesn't follow them."),
            ],
            entries);
    }

    [Fact]
    public async Task List_answers_a_link_that_leads_nowhere_as_unlisted()
    {
        //Arrange
        // A link to a disk that is away: what was found behind it before must be kept, so it isn't "empty".
        var link = Path.Combine(_media, "gone");
        FileLinks.CreateFolderLink(link, _folder.PathOf("nowhere"));
        _folder.WriteFile("1", "media", "movie.mkv");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [Problem(FileListingStatus.Unlisted, link, $"There is no file at “{link}”."), Media(_media, "movie.mkv")],
            entries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_follows_a_file_link_inside_the_root_by_the_links_own_name(bool relative)
    {
        //Arrange
        var file = _folder.WriteFile("12345", "media", "movie.mkv");
        if (!FileLinks.TryCreateSymbolicLink(Path.Combine(_media, "alias.mkv"), relative ? "movie.mkv" : file))
            Assert.Skip(FileLinks.SymbolicLinksUnavailable);
        // Named like no media file: the link's own name decides, not its target's.
        _ = FileLinks.TryCreateSymbolicLink(Path.Combine(_media, "alias.txt"), file);

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal([MediaFile(Path.Combine(_media, "alias.mkv"), file), MediaFile(file, file)], entries);
    }

    [Fact]
    public async Task List_refuses_a_file_link_leading_out()
    {
        //Arrange
        var secret = _folder.WriteFile("secret", "outside", "secret.mkv");
        var link = Path.Combine(_media, "movie.mkv");
        if (!FileLinks.TryCreateSymbolicLink(link, secret))
            Assert.Skip(FileLinks.SymbolicLinksUnavailable);

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [Problem(FileListingStatus.Skipped, link, $"“{link}” leads through a link to “{secret}”, outside the media folders the app may read.")],
            entries);
    }

    [Fact]
    public async Task List_answers_a_missing_root_as_unlisted_and_walks_the_next()
    {
        //Arrange
        var missing = _folder.PathOf("missing");
        _folder.WriteFile("1", "media", "movie.mkv");

        //Act
        var entries = await ListAsync(missing, _media);

        //Assert
        Assert.Equal(
            [Problem(FileListingStatus.Unlisted, missing, $"There is no file at “{missing}”."), Media(_media, "movie.mkv")],
            entries);
    }

    [Fact]
    public async Task List_answers_an_empty_root_as_unlisted()
    {
        //Arrange
        // An unmounted disk looks exactly like this.
        var root = _media;

        //Act
        var entries = await ListAsync(root);

        //Assert
        Assert.Equal(
            [Problem(FileListingStatus.Unlisted, root, $"“{root}” is empty; if it is a mount, it may be away, so the files found in it before are kept.")],
            entries);
    }

    [Fact]
    public async Task List_gives_the_names_under_a_root_that_is_a_link_as_the_root_was_typed()
    {
        //Arrange
        // The owner chose /library, and /library is a link to the disk's own folder.
        var file = _folder.WriteFile("12345", "disk", "movies", "movie.mkv");
        var library = _folder.PathOf("library");
        FileLinks.CreateFolderLink(library, _folder.PathOf("disk", "movies"));

        //Act
        var entries = await ListAsync(library);

        //Assert
        Assert.Equal([MediaFile(Path.Combine(library, "movie.mkv"), file)], entries);
    }

    [Fact]
    public async Task List_walks_the_roots_in_the_owners_order()
    {
        //Arrange
        var downloads = _folder.CreateFolder("downloads");
        _folder.WriteFile("1", "downloads", "a.mkv");
        _folder.WriteFile("2", "media", "b.mkv");

        //Act
        var entries = await ListAsync(_media, downloads);

        //Assert
        Assert.Equal([Media(_media, "b.mkv"), Media(downloads, "a.mkv")], entries);
    }

    [Fact]
    public async Task List_with_no_usable_root_gives_nothing()
    {
        //Arrange
        // A root can only narrow what is read: one that isn't a usable path of this platform allows nothing.
        _folder.WriteFile("1", "media", "movie.mkv");
        var otherStyle = OperatingSystem.IsWindows() ? "/media" : @"D:\media";
        var separator = Path.DirectorySeparatorChar;

        //Act
        var entries = await ListAsync("media", otherStyle, $"{_media}{separator}..{separator}media", "", " ");

        //Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task List_skips_a_name_whose_path_is_longer_than_4096_characters_and_everything_below_it()
    {
        //Arrange
        // A name's path is the root as typed and the names below it, while what is opened is where the links lead —
        // so links can make a path longer than any on the disk. Here the root is typed through a link back to its own
        // folder, 25 times (fewer than the 40 links a path may take), just short enough for a name of 5 characters
        // below it and too long for a longer one. The link's name keeps its own path under Windows' 260 characters.
        var parent = _folder.CreateFolder("disk");
        _folder.CreateFolder("disk", "media");
        const int hops = 25;
        var linkName = new string('l', (4040 - Canonical(Path.Combine(parent, "media")).Length) / hops - 1);
        FileLinks.CreateFolderLink(Path.Combine(parent, linkName), parent);
        var root = Path.Combine([parent, .. Enumerable.Repeat(linkName, hops), "media"]);
        var rootLength = Canonical(root).Length;
        // The root, a separator and this name: 4097 characters, one more than a row holds.
        var longName = new string('d', 4096 - rootLength);
        var shortFile = _folder.WriteFile("1", "disk", "media", "a.mkv");
        _folder.WriteFile("2", "disk", "media", longName, "inside.mkv");
        _folder.WriteFile("3", "disk", "media", longName[..^4] + ".mkv");

        //Act
        var entries = await ListAsync(root);

        //Assert
        Assert.InRange(rootLength, 4096 - 255, 4096 - 6);
        Assert.Equal(3, entries.Count);
        Assert.Equal(MediaFile(Path.Combine(root, "a.mkv"), shortFile), entries[0]);
        // The file whose name is as long, then the folder: neither is given, and nothing below the folder.
        Assert.Equal(Canonical(Path.Combine(root, longName[..^4] + ".mkv")), entries[1].AppPath);
        Assert.Equal(Canonical(Path.Combine(root, longName)), entries[2].AppPath);
        Assert.All(entries.Skip(1), entry =>
        {
            Assert.Equal(FileListingStatus.Skipped, entry.Status);
            Assert.Equal(4097, entry.AppPath.Length);
            Assert.Null(entry.ResolvedPath);
            Assert.StartsWith($"“{entry.AppPath[..200]}", entry.Error, StringComparison.Ordinal);
            Assert.EndsWith(PathTooLongEnding, entry.Error, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task List_stops_with_one_limit_reached_entry_past_its_entries()
    {
        //Arrange
        _folder.WriteFile("1", "media", "a.mkv");
        _folder.WriteFile("2", "media", "b.mkv");
        _folder.WriteFile("3", "media", "c.mkv");
        var inventory = new LocalFileInventory(new FfprobeProcess()) { MaximumListedEntries = 2 };

        //Act
        var entries = await ListAsync(inventory, _media);

        //Assert
        Assert.Equal(
            [
                Media(_media, "a.mkv"),
                Media(_media, "b.mkv"),
                Problem(FileListingStatus.LimitReached, Path.Combine(_media, "c.mkv"), string.Format(LimitReachedError, "2", "100,000")),
            ],
            entries);
    }

    [Fact]
    public async Task List_stops_with_one_limit_reached_entry_past_its_folders()
    {
        //Arrange
        // The root and "a" are two folders; "b" would be the third.
        _folder.WriteFile("1", "media", "a", "1.mkv");
        _folder.WriteFile("2", "media", "b", "2.mkv");
        var inventory = new LocalFileInventory(new FfprobeProcess()) { MaximumListedFolders = 2 };

        //Act
        var entries = await ListAsync(inventory, _media);

        //Assert
        Assert.Equal(
            [
                Media(_media, "a", "1.mkv"),
                Problem(FileListingStatus.LimitReached, Path.Combine(_media, "b"), string.Format(LimitReachedError, "200,000", "2")),
            ],
            entries);
    }

    [Fact]
    public async Task List_exactly_at_its_limits_gives_everything()
    {
        //Arrange
        _folder.WriteFile("1", "media", "a", "1.mkv");
        _folder.WriteFile("2", "media", "a", "2.mkv");
        _folder.WriteFile("3", "media", "a", "3.mkv");
        var inventory = new LocalFileInventory(new FfprobeProcess()) { MaximumListedFolders = 2, MaximumListedEntries = 3 };

        //Act
        var entries = await ListAsync(inventory, _media);

        //Assert
        Assert.Equal([Media(_media, "a", "1.mkv"), Media(_media, "a", "2.mkv"), Media(_media, "a", "3.mkv")], entries);
    }

    [Fact]
    public void LimitReached_with_the_walks_own_limits_names_200000_files_and_100000_folders()
    {
        //Arrange
        var maximumEntries = FolderWalker.MaximumEntries;
        var maximumFolders = FolderWalker.MaximumFolders;

        //Act
        var sentence = FileInventoryMessages.LimitReached(maximumEntries, maximumFolders);

        //Assert
        Assert.Equal(string.Format(LimitReachedError, "200,000", "100,000"), TestTexts.En(sentence));
    }

    [Fact]
    public async Task List_skips_a_folder_with_the_identity_of_one_above_it()
    {
        //Arrange
        // What a bind mount of /media inside /media/Film looks like: another path, the same folder. Making one
        // needs root, so a reader that reports the identities stands in for the Linux one.
        _folder.WriteFile("1", "media", "Film", "movie.mkv");
        _folder.WriteFile("2", "media", "Film", "mount", "again.mkv");
        var reader = new IdentityByNameReader(new Dictionary<string, long>(StringComparer.Ordinal) { ["media"] = 1, ["Film"] = 2, ["mount"] = 1 });
        NormalizedPath.TryParse(_media, out var root, out _);
        var mount = Path.Combine(_media, "Film", "mount");

        //Act
        var entries = new List<Listed>();
        await foreach (var entry in FolderWalker.WalkAsync([root!], reader, FolderWalker.MaximumFolders, FolderWalker.MaximumEntries, TestContext.Current.CancellationToken))
            entries.Add(InEnglish(entry));

        //Assert
        Assert.Equal(
            [
                Problem(FileListingStatus.Skipped, mount, $"“{mount}” leads back to a folder above it, so the app doesn't walk it again."),
                Media(_media, "Film", "movie.mkv"),
            ],
            entries);
    }

    [Fact]
    public async Task List_gives_the_names_of_the_folder_it_opened_under_the_folders_own_path()
    {
        //Arrange
        // What a folder swapped after it was opened looks like: the reader opened "b", and by the time it is listed its
        // path leads to another folder. Only Linux can hold a folder open, so a reader that hands back another folder
        // stands in for the swap — what is listed is what was opened, and its names are given under b's own path, never
        // by the full names the listing reports.
        _folder.WriteFile("1", "media", "a.mkv");
        _folder.WriteFile("2", "media", "b", "now-at-b.mkv");
        var opened = _folder.CreateFolder("opened");
        _folder.WriteFile("3", "opened", "opened.mkv");
        var reader = new OpensAnotherFolderReader("b", opened);
        NormalizedPath.TryParse(_media, out var root, out _);

        //Act
        var entries = new List<Listed>();
        await foreach (var entry in FolderWalker.WalkAsync([root!], reader, FolderWalker.MaximumFolders, FolderWalker.MaximumEntries, TestContext.Current.CancellationToken))
            entries.Add(InEnglish(entry));

        //Assert
        // Nothing of what is at b's path now ("now-at-b.mkv"), and nothing under the opened folder's own path.
        Assert.Equal([Media(_media, "a.mkv"), Media(_media, "b", "opened.mkv")], entries);
        Assert.Equal(["media", "b"], reader.Opened.Select(Path.GetFileName));
    }

    // MAM-26 — the quarantine holds the replaced copies, which the replacement jobs know (ADR 0030): the walk passes
    // it over in silence, so a quarantined video is never a file no library reported, a possible match or part of a
    // Media folders count, and never a problem of the scan.

    [Fact]
    public async Task List_never_walks_a_folder_named_mam_quarantine_at_any_depth_and_counts_no_problem()
    {
        //Arrange
        _folder.WriteFile("1", "media", "a.mkv");
        _folder.WriteFile("2", "media", ".mam-quarantine", "6f9d2c1e-4b7a-4e10-9c55-0d3a8e1f2b64", "movies", "Alien (1979)", "Alien.mkv");
        _folder.WriteFile("3", "media", "movies", ".mam-quarantine", "deep", "Old.mkv");
        _folder.WriteFile("4", "media", "movies", "z.mkv");
        // A file of that name is passed over too: no entry of that name is ever given.
        _folder.WriteFile("5", "media", "shows", ".mam-quarantine");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal([Media(_media, "a.mkv"), Media(_media, "movies", "z.mkv")], entries);
    }

    [Fact]
    public async Task List_never_follows_a_link_into_a_quarantine()
    {
        //Arrange
        // Links with ordinary names, inside the root, leading to the quarantine and into a job's folder in it.
        _folder.WriteFile("1", "media", ".mam-quarantine", "job", "Alien.mkv");
        _folder.WriteFile("2", "media", "movies", "z.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "kept"), _folder.PathOf("media", ".mam-quarantine"));
        FileLinks.CreateFolderLink(Path.Combine(_media, "movies", "old"), _folder.PathOf("media", ".mam-quarantine", "job"));
        _ = FileLinks.TryCreateSymbolicLink(Path.Combine(_media, "alias.mkv"), _folder.PathOf("media", ".mam-quarantine", "job", "Alien.mkv"));

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal([Media(_media, "movies", "z.mkv")], entries);
    }

    [Fact]
    public async Task List_of_a_root_that_lies_in_a_quarantine_gives_nothing_for_it_and_walks_the_others()
    {
        //Arrange
        // A root typed inside a quarantine, and one that is a link into one.
        var downloads = _folder.CreateFolder("downloads");
        _folder.WriteFile("1", "downloads", "a.mkv");
        var typedInside = _folder.CreateFolder("media", ".mam-quarantine", "job");
        _folder.WriteFile("2", "media", ".mam-quarantine", "job", "Alien.mkv");
        var linkedInside = _folder.PathOf("library");
        FileLinks.CreateFolderLink(linkedInside, typedInside);

        //Act
        var entries = await ListAsync(typedInside, linkedInside, downloads);

        //Assert
        Assert.Equal([Media(downloads, "a.mkv")], entries);
    }

    [Fact]
    public async Task List_cancelled_before_it_starts_throws()
    {
        //Arrange
        _folder.WriteFile("1", "media", "movie.mkv");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in _inventory.ListMediaFilesAsync([_media], cancellation.Token))
            {
            }
        });

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task List_cancelled_during_the_walk_throws_before_the_next_folder()
    {
        //Arrange
        _folder.WriteFile("1", "media", "a", "1.mkv");
        _folder.WriteFile("2", "media", "b", "2.mkv");
        using var cancellation = new CancellationTokenSource();
        await using var walk = _inventory.ListMediaFilesAsync([_media], cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        await walk.MoveNextAsync();
        var first = InEnglish(walk.Current);
        await cancellation.CancelAsync();

        //Act
        var exception = await Record.ExceptionAsync(async () => await walk.MoveNextAsync());

        //Assert
        Assert.Equal(Media(_media, "a", "1.mkv"), first);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<List<Listed>> ListAsync(params string[] roots) => ListAsync(_inventory, roots);

    private static async Task<List<Listed>> ListAsync(LocalFileInventory inventory, params string[] roots)
    {
        var entries = new List<Listed>();
        await foreach (var entry in inventory.ListMediaFilesAsync(roots, TestContext.Current.CancellationToken))
            entries.Add(InEnglish(entry));
        return entries;
    }

    /// <summary>A media file name reached by its own path: where it is, is where its name says.</summary>
    private static Listed Media(string folder, params string[] names)
    {
        var path = Path.Combine([folder, .. names]);
        return MediaFile(path, path);
    }

    /// <summary>A media file name reached through a link, or given with where it is.</summary>
    private static Listed MediaFile(string appPath, string resolvedPath) =>
        (FileListingStatus.MediaFile, Canonical(appPath), Canonical(resolvedPath), null);

    private static Listed Problem(FileListingStatus status, string appPath, string error) =>
        (status, Canonical(appPath), null, error);

    /// <summary>The path in the form the walk gives it (the drive letter in capitals on Windows).</summary>
    private static string Canonical(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed.ToString() : throw new ArgumentException($"“{path}” is not a full path.", nameof(path));

    /// <summary>
    /// Lists folders by their path, as the portable reader does, and gives each the identity its name maps to: two
    /// folders with one identity are one folder reached twice.
    /// </summary>
    private sealed class IdentityByNameReader(IReadOnlyDictionary<string, long> inodes) : IFileStatusReader
    {
        public bool ReportsFileIdentity => true;

        public FileInventoryResult<IOpenedFile> Open(string systemPath, string appPath) =>
            throw new NotSupportedException("The walk opens no file.");

        public FileInventoryResult<IOpenedFolder> OpenFolder(string systemPath, string appPath) =>
            new(FileInventoryStatus.Found, new FolderWithIdentity(systemPath, inodes[Path.GetFileName(systemPath)]), null);
    }

    /// <summary>
    /// Lists folders by their path, as the portable reader does, except the folder named <paramref name="swappedName"/>:
    /// for it, <paramref name="opened"/> is listed — the folder that was at its path when it was opened.
    /// </summary>
    private sealed class OpensAnotherFolderReader(string swappedName, string opened) : IFileStatusReader
    {
        /// <summary>Every folder the walk asked to open, in order.</summary>
        public List<string> Opened { get; } = [];

        public bool ReportsFileIdentity => false;

        public FileInventoryResult<IOpenedFile> Open(string systemPath, string appPath) =>
            throw new NotSupportedException("The walk opens no file.");

        public FileInventoryResult<IOpenedFolder> OpenFolder(string systemPath, string appPath)
        {
            Opened.Add(systemPath);
            var listed = Path.GetFileName(systemPath) == swappedName ? opened : systemPath;
            return new(FileInventoryStatus.Found, new FolderListedByPath(listed), null);
        }
    }

    /// <summary>A folder listed by its path, with the identity the test gave it.</summary>
    private sealed class FolderWithIdentity(string listPath, long inode) : IOpenedFolder
    {
        public string ListPath => listPath;

        public long? DeviceId => 1;

        public long? Inode => inode;

        public void Dispose()
        {
        }
    }

    /// <summary>MAM-47 — an entry with its message in English: a refusal of the walk compares by the words a reader
    /// sees.</summary>
    private static Listed InEnglish(FileListingEntry entry) => (entry.Status, entry.AppPath, entry.ResolvedPath, TestTexts.En(entry.Error));
}
