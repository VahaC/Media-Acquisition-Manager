using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Api.Tests.LibraryLinks;
using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042, R22, design §6.2–§6.6, §6.8) — the startup converter over the real SQLite of the test host: the English
// an older build stored in each message column becomes the key and values it was written from, exactly; a text cut at its
// column's limit inside its last value converts with that value cut, one cut in its words is kept; a reading too long for
// the column as JSON is fitted; a service's words and an ambiguous text are kept as their words, marked with the
// converter's version and tried again only by a newer one; a second start changes nothing; a null, an empty text and one
// that only opens like a message stay as they are; a column that fails is logged and left while the others convert, and
// one that fails in the middle of a page keeps the pages written before it, rolls that page back whole and leaves every
// row readable for the next start to finish; the log names counts and rows, never a text; the audit rows of the owner's
// confirmations are named by their claim (Q1). Rows are planted the way an older build left them — raw text in the
// column, through SQL — and with every other required column filled and foreign keys off where the converter reads only
// the column; the two state-bearing link rows of §6.6 are real rows, read back by the code that relies on them.
public sealed class StoredTextConverterTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string CatchUpUrl = "/api/library/tmdb/catch-up";
    private const string AlienPath = "/media/Movies/Alien (1979)/Alien (1979).mkv";
    private const string BusyWords = "Device or resource busy (errno 16)";

    /// <summary>The message the older build wrote into each message column, whose English is planted there.</summary>
    private static readonly Dictionary<(string Table, string Column), Message> OlderEnglish = new()
    {
        [("CopyRemovalNames", "Problem")] = Message.Of("files.quarantine.could-not-change", ("path", AlienPath), ("reason", VendorText.Clean(BusyWords, 300))),
        [("CopyRemovals", "Reason")] = Message.Of("replacements.quarantine.cut.moving-removal"),
        [("DownloadJobs", "BlockReason")] = Message.Of("downloads.follow.missing", ("client", "qBittorrent")),
        [("DownloadJobs", "Error")] = TorznabFailed("Download failed"),
        [("FileInspectionRuns", "Error")] = Message.Of("files.inspection.restarted"),
        [("FileInspectionRuns", "HashProblem")] = Message.Of("files.inventory.changed-while-hashing", ("path", AlienPath)),
        [("FolderScanRuns", "Error")] = Message.Of("files.scan.restarted"),
        [("FolderScanRuns", "Problem")] = Message.Of("files.inventory.empty-root", ("path", "/media/Movies")),
        [("LibraryItemLinks", "LookupError")] = Message.Of("integrations.catalog.not-connected"),
        [("LibraryItemLinks", "WriteError")] = Message.Of("library-links.write.switched-off", ("integration", "Jellyfin")),
        [("LibraryLinkSuggestionRuns", "Error")] = Message.Of("library-links.suggestions.restarted"),
        [("LibrarySyncRuns", "Error")] = Message.Of("library.sync.restarted"),
        [("MediaInspections", "Error")] = Message.Of(
            "files.inventory.media-unreadable",
            ("path", AlienPath),
            ("reason", Message.Of("files.ffprobe.too-much-output-clause", ("limit", new SizeValue(8 * 1024 * 1024))))),
        [("PhysicalFiles", "InspectionError")] = Message.Of("files.inventory.not-found", ("path", AlienPath)),
        [("RecommendationRuns", "Error")] = Message.Of("recommendations.run.restarted"),
        [("ReplacementJobFiles", "Problem")] = Message.Of("replacements.quarantine.check.gone", ("path", AlienPath)),
        [("ReplacementJobNames", "Problem")] = Message.Of("files.quarantine.read-only", ("path", "/media/Movies/Alien (1979)/Alien (1979).srt")),
        [("ReplacementJobNewFiles", "Problem")] = Message.Of("replacements.readability.no-video", ("path", "/media/Movies/Alien (1979)/Alien.1979.2160p.mkv")),
        [("ReplacementJobWorks", "ConfirmationText")] = ReplacementConfirmation.Of("Doctor Who", 2005, new ClaimedEpisodes(2, 2, 1, 5)),
        [("ReplacementJobs", "Reason")] = Message.Of(
            "replacements.pairing.new-files-film",
            ("count", 2),
            ("work", "Alien (1979)"),
            ("paths", new ListValue([new TextValue("/media/movies/Alien.2160p.mkv"), new TextValue("/media/movies/Alien.1080p.mkv")]))),
        [("SearchSources", "Error")] = Message.Of("integrations.adapter.unreadable-answer", ("name", "Prowlarr")),
    };

    private readonly MediaAcquisitionManagerApiFactory _factory;

    public StoredTextConverterTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync() => await _factory.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Convert_turns_the_older_english_of_every_message_column_into_its_key_and_values()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = new Dictionary<(string Table, string Column), long>();
        foreach (var ((table, column), message) in OlderEnglish)
            planted[(table, column)] = await PlantAsync(table, column, TestTexts.En(message)!, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        // Guard of the guard: every column the converter walks from the model has a planted row here, and no other.
        Assert.Equal(
            MessageColumnsOfTheModel().Order(StringComparer.Ordinal).Append("AuditEvents.TargetName"),
            result.Columns.Select(column => $"{column.Table}.{column.Column}"));
        Assert.Equal(
            OlderEnglish.Keys.Select(key => $"{key.Table}.{key.Column}").Order(StringComparer.Ordinal),
            MessageColumnsOfTheModel().Order(StringComparer.Ordinal));
        foreach (var ((table, column), message) in OlderEnglish)
        {
            var rowId = planted[(table, column)];
            Assert.Equal((MessageJson.Write(message), false), (await StoredAsync(table, column, rowId, cancellationToken), result.Of(table, column)!.Failed));
            Assert.Equal([new StoredTextRowResult(rowId, ConversionOutcome.Converted)], result.Of(table, column)!.Rows);
        }
    }

    [Fact]
    public async Task Convert_writes_back_the_very_words_that_were_stored_when_the_message_is_rendered_in_english()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var english = TestTexts.En(OlderEnglish[("ReplacementJobs", "Reason")])!;
        var rowId = await PlantAsync("ReplacementJobs", "Reason", english, cancellationToken);

        //Act
        Convert();

        //Assert
        Assert.Equal(
            ("Two new files are Alien (1979): “/media/movies/Alien.2160p.mkv” and “/media/movies/Alien.1080p.mkv”; the app can't tell which one replaces the old copy.", english),
            (english, TestTexts.En(MessageJson.TryRead(await StoredAsync("ReplacementJobs", "Reason", rowId, cancellationToken)))));
    }

    // MAM-47 WP14 review — an older build wrote a work with no title as the label's English words where a name goes; they
    // read back as the label, alone or in a list of names, so a reader in another language sees the label in that
    // language («без назви (2005)»), never the English words.
    [Fact]
    public async Task Convert_reads_a_work_with_no_title_back_as_its_label_in_a_name_and_in_a_list_of_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var named = Message.Of("replacements.pairing.no-new-file-film", ("work", ReplacementPairingWork.NameOf(null, 2005)), ("path", AlienPath));
        var listed = Message.Of(
            "replacements.pairing.film-without-id",
            ("library", "Jellyfin"),
            ("path", AlienPath),
            ("works", new ListValue([ReplacementPairingWork.NameOf("Alien", 1979), ReplacementPairingWork.NameOf(null, null)])));
        var namedRow = await PlantAsync("ReplacementJobs", "Reason", TestTexts.En(named)!, cancellationToken);
        var listedRow = await PlantAsync("ReplacementJobs", "Reason", TestTexts.En(listed)!, cancellationToken);

        //Act
        Convert();

        //Assert
        Assert.Equal(
            (MessageJson.Write(named), MessageJson.Write(listed)),
            (await StoredAsync("ReplacementJobs", "Reason", namedRow, cancellationToken), await StoredAsync("ReplacementJobs", "Reason", listedRow, cancellationToken)));
    }

    [Fact]
    public async Task Convert_of_a_text_cut_at_the_column_limit_inside_its_last_value_converts_it_with_that_value_cut_and_fitted()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // What the feed said ran past the column, and the older build cut the whole sentence to 999 characters and "…".
        var whole = TestTexts.En(TorznabFailed(string.Join(", ", Enumerable.Repeat("the tracker sent its maintenance page instead of the torrent", 30))))!;
        var cut = string.Concat(whole.AsSpan(0, MessageJson.ColumnLength - 1), "…");
        var rowId = await PlantAsync("DownloadJobs", "Error", cut, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal([new StoredTextRowResult(rowId, ConversionOutcome.ConvertedWithCutValue, Shortened: true)], result.Of("DownloadJobs", "Error")!.Rows);
        var stored = await StoredAsync("DownloadJobs", "Error", rowId, cancellationToken);
        var message = MessageJson.TryRead(stored)!;
        var english = TestTexts.En(message)!;
        // The feed's words keep the cut, shortened again so the JSON fits the column; the template's own words follow it.
        Assert.Equal(("torznab.download.failed", true, true), (message.Key, stored.Length <= MessageJson.ColumnLength, english.EndsWith("…).", StringComparison.Ordinal)));
        Assert.StartsWith(english[..^"…).".Length], cut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_of_a_text_cut_at_the_column_limit_in_its_words_keeps_it_as_its_words()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The path ends before the cut, which falls in the sentence's own words after it.
        var whole = TestTexts.En(Message.Of("files.inventory.empty-root", ("path", "/media/" + new string('m', 950))))!;
        var cut = string.Concat(whole.AsSpan(0, MessageJson.ColumnLength - 1), "…");
        var rowId = await PlantAsync("FolderScanRuns", "Problem", cut, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal([new StoredTextRowResult(rowId, ConversionOutcome.KeptCut)], result.Of("FolderScanRuns", "Problem")!.Rows);
        Assert.Equal(MessageJson.WriteKept(cut, StoredTextConverter.Version), await StoredAsync("FolderScanRuns", "Problem", rowId, cancellationToken));
    }

    [Fact]
    public async Task Convert_of_a_reading_too_long_for_the_column_as_json_cuts_its_longest_value_to_fit()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // 993 characters of English, but more than 1000 as JSON with its key.
        var path = "/media/" + new string('p', 963);
        var english = TestTexts.En(Message.Of("files.inventory.not-found", ("path", path)))!;
        var rowId = await PlantAsync("PhysicalFiles", "InspectionError", english, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal([new StoredTextRowResult(rowId, ConversionOutcome.Converted, Shortened: true)], result.Of("PhysicalFiles", "InspectionError")!.Rows);
        var stored = await StoredAsync("PhysicalFiles", "InspectionError", rowId, cancellationToken);
        var fitted = (TextValue)MessageJson.TryRead(stored)!.Values["path"];
        Assert.Equal(
            (true, true, true, true),
            (english.Length < MessageJson.ColumnLength, stored.Length <= MessageJson.ColumnLength, fitted.Text.EndsWith('…'), path.StartsWith(fitted.Text[..^1], StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(BusyWords, ConversionOutcome.Kept)]
    [InlineData("Download failed", ConversionOutcome.Kept)]
    // A service's JSON answer, stored as its words: JSON, but not a message.
    [InlineData("{\"error\":\"Indexer unavailable\",\"code\":503}", ConversionOutcome.Kept)]
    // Two readings: the method and the path are told apart by a space alone (the round trip's allowlist names this pair).
    [InlineData("No API endpoint matches GET /api/nope deeper.", ConversionOutcome.KeptAmbiguous)]
    public async Task Convert_keeps_a_text_it_cant_read_back_as_its_words_marked_with_its_version(string words, ConversionOutcome outcome)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var rowId = await PlantAsync("CopyRemovalNames", "Problem", words, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal([new StoredTextRowResult(rowId, outcome)], result.Of("CopyRemovalNames", "Problem")!.Rows);
        var stored = await StoredAsync("CopyRemovalNames", "Problem", rowId, cancellationToken);
        Assert.Equal((MessageJson.WriteKept(words, StoredTextConverter.Version), StoredTextConverter.Version), (stored, MessageJson.KeptVersionOf(stored)));
        // The column reads it as the words they were: a reader sees what he saw before.
        Message? read = null;
        await _factory.WithDbAsync(async db => read = await db.CopyRemovalNames.Select(name => name.Problem).SingleAsync(cancellationToken));
        Assert.Equal(Message.Legacy(words), read);
    }

    [Fact]
    public async Task Convert_run_again_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var ((table, column), message) in OlderEnglish)
            await PlantAsync(table, column, TestTexts.En(message)!, cancellationToken);
        await PlantAsync("DownloadJobs", "Error", BusyWords, cancellationToken);
        await PlantAuditNameAsync(AuditAction.ReplacementWorkConfirmed, "This release is Alien (1979)", cancellationToken);
        Convert();
        var before = await SnapshotAsync(cancellationToken);

        //Act
        var again = Convert();

        //Assert
        Assert.All(again.Columns, column => Assert.Equal((0, false), (column.Rows.Count, column.Failed)));
        Assert.Equal(before, await SnapshotAsync(cancellationToken));
    }

    [Fact]
    public async Task Convert_tries_again_a_text_kept_under_an_older_version_and_leaves_one_kept_under_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var message = OlderEnglish[("SearchSources", "Error")];
        var english = TestTexts.En(message)!;
        var olderVersion = await PlantAsync("SearchSources", "Error", MessageJson.WriteKept(english, StoredTextConverter.Version - 1), cancellationToken);
        var unmarked = await PlantAsync("LibrarySyncRuns", "Error", MessageJson.Write(Message.Legacy(TestTexts.En(OlderEnglish[("LibrarySyncRuns", "Error")])!)), cancellationToken);
        var ownVersion = MessageJson.WriteKept(english, StoredTextConverter.Version);
        var current = await PlantAsync("DownloadJobs", "Error", ownVersion, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal(
            (MessageJson.Write(message), MessageJson.Write(OlderEnglish[("LibrarySyncRuns", "Error")]), ownVersion),
            (await StoredAsync("SearchSources", "Error", olderVersion, cancellationToken),
                await StoredAsync("LibrarySyncRuns", "Error", unmarked, cancellationToken),
                await StoredAsync("DownloadJobs", "Error", current, cancellationToken)));
        Assert.Equal([new StoredTextRowResult(olderVersion, ConversionOutcome.Converted)], result.Of("SearchSources", "Error")!.Rows);
        Assert.Equal([new StoredTextRowResult(unmarked, ConversionOutcome.Converted)], result.Of("LibrarySyncRuns", "Error")!.Rows);
        Assert.Empty(result.Of("DownloadJobs", "Error")!.Rows);
    }

    [Fact]
    public async Task Convert_leaves_the_messages_the_app_wrote_and_empty_columns_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var message = OlderEnglish[("DownloadJobs", "BlockReason")];
        var written = await PlantAsync("DownloadJobs", "BlockReason", MessageJson.Write(message), cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal(MessageJson.Write(message), await StoredAsync("DownloadJobs", "BlockReason", written, cancellationToken));
        Assert.All(result.Columns, column => Assert.Empty(column.Rows));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category == typeof(StoredTextConverter).FullName);
    }

    [Fact]
    public async Task Convert_leaves_null_empty_and_message_like_texts_as_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Opens like a message, but its key is none: the converter takes it for one, the column reads it as its words.
        const string MessageLike = "{\"k\":\"Not a key\"}";
        await PlantAsync("DownloadJobs", "Error", "", cancellationToken);
        await PlantAsync("DownloadJobs", "BlockReason", MessageLike, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.All(result.Columns, column => Assert.Equal((0, false), (column.Rows.Count, column.Failed)));
        // Each row keeps its null beside the text: the converter writes no column it didn't read.
        var stored = new List<string>();
        var read = new List<(Message? Error, Message? BlockReason)>();
        await _factory.WithDbAsync(async db =>
        {
            stored.AddRange(await db.Database
                .SqlQueryRaw<string>("SELECT quote(\"Error\") || ' ' || quote(\"BlockReason\") AS \"Value\" FROM \"DownloadJobs\" ORDER BY rowid")
                .ToListAsync(cancellationToken));
            read.AddRange((await db.DownloadJobs.Select(job => new { job.Error, job.BlockReason }).ToListAsync(cancellationToken)).Select(job => (job.Error, job.BlockReason)));
        });
        Assert.Equal(["'' NULL", $"NULL '{MessageLike}'"], stored);
        (Message? Error, Message? BlockReason)[] expected = [(Message.Legacy(""), null), (null, Message.Legacy(MessageLike))];
        Assert.Equal(expected, read.OrderBy(row => row.Error is null));
    }

    [Fact]
    public async Task Convert_failing_in_the_middle_of_a_page_keeps_the_pages_before_it_and_every_row_readable()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantTwoPagesAsync(cancellationToken);
        // The second page's last row: the page's first row is written already, inside the page's transaction.
        FailTheWriteOf(planted[^1].RowId);

        //Act
        var result = Convert();

        //Assert
        // The page before the failure is written; the failing page is rolled back whole, the write before the failure
        // included, and left to the next start.
        var column = result.Of("PhysicalFiles", "InspectionError")!;
        Assert.True(column.Failed);
        Assert.Equal(planted.Take(StoredTextConverter.PageSize).Select(row => row.RowId), column.Rows.Select(row => row.RowId));
        for (var index = 0; index < planted.Count; index++)
        {
            var expected = index < StoredTextConverter.PageSize ? MessageJson.Write(NotFound(index)) : planted[index].English;
            Assert.Equal(expected, await StoredAsync("PhysicalFiles", "InspectionError", planted[index].RowId, cancellationToken));
        }

        // Converted and unconverted rows side by side, every one read through the column as the very words stored.
        Assert.Equal(planted.Select(row => row.English).Order(StringComparer.Ordinal), (await InspectionErrorsAsync(cancellationToken)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Convert_after_a_run_that_failed_in_the_middle_of_a_page_converts_the_rest()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var planted = await PlantTwoPagesAsync(cancellationToken);
        FailTheWriteOf(planted[^1].RowId);
        Convert();

        //Act
        var result = Convert();

        //Assert
        var column = result.Of("PhysicalFiles", "InspectionError")!;
        Assert.False(column.Failed);
        Assert.Equal(planted.Skip(StoredTextConverter.PageSize).Select(row => row.RowId), column.Rows.Select(row => row.RowId));
        for (var index = 0; index < planted.Count; index++)
            Assert.Equal(MessageJson.Write(NotFound(index)), await StoredAsync("PhysicalFiles", "InspectionError", planted[index].RowId, cancellationToken));
        Assert.Equal(planted.Select(row => row.English).Order(StringComparer.Ordinal), (await InspectionErrorsAsync(cancellationToken)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Convert_reads_more_rows_than_a_page_holds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var rowIds = new List<long>();
        for (var number = 0; number < StoredTextConverter.PageSize + 2; number++)
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"/media/Movies/Film {number}.mkv");
            rowIds.Add(await PlantAsync("PhysicalFiles", "InspectionError", TestTexts.En(Message.Of("files.inventory.not-found", ("path", path)))!, cancellationToken));
        }

        //Act
        var result = Convert();

        //Assert
        Assert.Equal(rowIds, result.Of("PhysicalFiles", "InspectionError")!.Rows.Select(row => row.RowId));
        Assert.Equal(
            MessageJson.Write(Message.Of("files.inventory.not-found", ("path", $"/media/Movies/Film {StoredTextConverter.PageSize + 1}.mkv"))),
            await StoredAsync("PhysicalFiles", "InspectionError", rowIds[^1], cancellationToken));
    }

    [Fact]
    public async Task Convert_logs_a_column_whose_read_fails_and_converts_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var english = TestTexts.En(OlderEnglish[("SearchSources", "Error")])!;
        var failing = await PlantAsync("SearchSources", "Error", english, cancellationToken);
        var other = await PlantAsync("LibrarySyncRuns", "Error", TestTexts.En(OlderEnglish[("LibrarySyncRuns", "Error")])!, cancellationToken);
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("FROM \"SearchSources\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("The disk went away."));

        //Act
        var result = Convert();

        //Assert
        Assert.Equal((true, 0), (result.Of("SearchSources", "Error")!.Failed, result.Of("SearchSources", "Error")!.Rows.Count));
        Assert.Equal(english, await StoredAsync("SearchSources", "Error", failing, cancellationToken));
        Assert.Equal(MessageJson.Write(OlderEnglish[("LibrarySyncRuns", "Error")]), await StoredAsync("LibrarySyncRuns", "Error", other, cancellationToken));
        var error = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(StoredTextConverter).FullName && entry.Level == LogLevel.Error);
        Assert.Equal(
            ("The conversion of the stored texts of SearchSources.Error failed; they are shown as stored until the next start.", "The disk went away."),
            (error.Message, error.Exception?.Message));
    }

    [Fact]
    public async Task Convert_logs_counts_and_row_ids_and_never_a_text()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantAsync("PhysicalFiles", "InspectionError", TestTexts.En(OlderEnglish[("PhysicalFiles", "InspectionError")])!, cancellationToken);
        var kept = await PlantAsync("PhysicalFiles", "InspectionError", BusyWords, cancellationToken);
        var shortened = await PlantAsync("PhysicalFiles", "InspectionError", TestTexts.En(Message.Of("files.inventory.not-found", ("path", "/media/" + new string('p', 963))))!, cancellationToken);

        //Act
        Convert();

        //Assert
        var logged = _factory.Logs.Entries.Where(entry => entry.Category == typeof(StoredTextConverter).FullName).ToList();
        Assert.Equal(
            [
                (LogLevel.Information, "Converted 2 stored texts of PhysicalFiles.InspectionError; 0 with a cut value, 1 shortened to fit, 1 kept as their words."),
                (LogLevel.Information, $"Rows of PhysicalFiles.InspectionError kept as their words: {kept}; shortened to fit: {shortened}."),
            ],
            logged.Select(entry => (entry.Level, entry.Message)));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains("Alien", StringComparison.Ordinal) || entry.Message.Contains("errno", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("This release is Alien (1979)", "Alien (1979)")]
    [InlineData("This release is Doctor Who (2005), season 2, episodes 1–5", "Doctor Who (2005) S02E01–E05")]
    [InlineData("This release is a work with no title, season 1 in “Серіали”", "— S01 · Серіали")]
    [InlineData("This release is Halloween (2018) in “Фільми” (the copy “Halloween.2018.1080p.mkv”)", "Halloween (2018) · Фільми · Halloween.2018.1080p.mkv")]
    [InlineData("This release is Halloween (2018) (2 of 2)", "Halloween (2018) · 2/2")]
    public async Task Convert_names_an_older_builds_confirmation_audit_row_by_its_claim(string sentence, string label)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var rowId = await PlantAuditNameAsync(AuditAction.ReplacementWorkConfirmed, sentence, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal(label, await StoredAsync("AuditEvents", "TargetName", rowId, cancellationToken));
        Assert.Equal([new StoredTextRowResult(rowId, ConversionOutcome.Converted)], result.Of("AuditEvents", "TargetName")!.Rows);
        Assert.Contains(
            _factory.Logs.Entries,
            entry => entry.Message == "Named 1 owner's confirmations in AuditEvents.TargetName by their claim; 0 left as they were.");
    }

    [Fact]
    public async Task Convert_names_an_audit_row_cut_at_its_limit_by_the_cut_claim()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The copy's name ran past the 200 characters, and the older build cut the sentence to 199 and "…".
        const string Opening = "This release is Halloween (2018) (the copy “";
        var cut = string.Concat($"{Opening}{new string('n', 300)}”)".AsSpan(0, 199), "…");
        var rowId = await PlantAuditNameAsync(AuditAction.ReplacementWorkConfirmed, cut, cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal($"Halloween (2018) · {cut[Opening.Length..]}", await StoredAsync("AuditEvents", "TargetName", rowId, cancellationToken));
        Assert.Equal([new StoredTextRowResult(rowId, ConversionOutcome.ConvertedWithCutValue)], result.Of("AuditEvents", "TargetName")!.Rows);
    }

    [Fact]
    public async Task Convert_leaves_other_audit_rows_and_a_name_it_cant_read_back_as_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var unread = await PlantAuditNameAsync(AuditAction.ReplacementWorkConfirmed, "This release is ", cancellationToken);
        var otherAction = await PlantAuditNameAsync(AuditAction.ReplacementScheduled, "This release is Alien (1979)", cancellationToken);
        var label = await PlantAuditNameAsync(AuditAction.ReplacementWorkConfirmed, "Alien (1979)", cancellationToken);

        //Act
        var result = Convert();

        //Assert
        Assert.Equal(
            ("This release is ", "This release is Alien (1979)", "Alien (1979)"),
            (await StoredAsync("AuditEvents", "TargetName", unread, cancellationToken),
                await StoredAsync("AuditEvents", "TargetName", otherAction, cancellationToken),
                await StoredAsync("AuditEvents", "TargetName", label, cancellationToken)));
        // Reported at every start, never rewritten: the audit name is a name, not a message column.
        Assert.Equal([new StoredTextRowResult(unread, ConversionOutcome.Kept)], result.Of("AuditEvents", "TargetName")!.Rows);
    }

    [Fact]
    public async Task Convert_of_the_links_older_english_gives_the_keys_the_repair_and_the_writer_rely_on()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var library = await new IntegrationConnectionSeeder(_factory.Scope).Create(FakeLibraryItemIdentifier.TypeId, "Attic");
        var work = await new MediaWorkSeeder(_factory.Scope).CreateMovie(library, "Landing Stripling", 1962);
        var links = new LibraryItemLinkSeeder(_factory.Scope);
        var restarted = await links.Create(library, "item-restarted", work.Id, MediaIdentifierScheme.Tmdb, "348", writeState: LibraryLinkWriteState.NotWritten);
        var switchedOff = await links.Create(library, "item-switched-off", work.Id, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.NotWritten);
        var episode = await links.Create(
            library, "item-episode", work.Id, MediaIdentifierScheme.TmdbTv, "1399", LibraryLinkTarget.ShowEpisode, LibraryLinkWriteState.NotWritten,
            configure: row => (row.SeasonNumber, row.EpisodeNumber) = (1, 2));
        // The wordings are unchanged since fe6897e, so an older build stored exactly today's English.
        await SetLinkEnglishAsync(restarted.Id, "The app restarted while identifying the item in Jellyfin. Jellyfin may still be working on it: Retry in a few minutes checks what it holds before anything is sent.", cancellationToken);
        await SetLinkEnglishAsync(switchedOff.Id, "“Attic” is switched off. Switch it on under Integrations, then retry.", cancellationToken);
        await SetLinkEnglishAsync(episode.Id, "TMDb knows no IMDb id of this episode, so its link stays in the app.", cancellationToken);

        //Act
        Convert();

        //Assert
        var stored = (await _factory.LinksAsync(cancellationToken)).ToDictionary(link => link.Id);
        Assert.Equal(
            ("library-links.write.restarted", "library-links.write.switched-off", "library-links.kept.no-episode-imdb"),
            (stored[restarted.Id].WriteError?.Key, stored[switchedOff.Id].WriteError?.Key, stored[episode.Id].WriteError?.Key));
        // The writer knows TMDb was asked and gave the episode no IMDb id, so it doesn't ask again.
        Assert.True(LibraryLinkWritability.KnowsNoEpisodeImdb(stored[episode.Id]));
        // The repair leaves the restarted link to the owner's own Retry, and takes the other.
        var response = await _factory.GetHttpRequest(CatchUpUrl, owner.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new LibraryLinkCatchUpResponse(1, null),
            await response.Content.ReadFromJsonAsync<LibraryLinkCatchUpResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Message TorznabFailed(string said) =>
        Message.Of(
            "torznab.download.failed",
            ("error", Message.Of("torznab.error.described-clause", ("code", 500), ("description", VendorText.Clean(said, 4000)))));

    private static Message NotFound(int number) =>
        Message.Of("files.inventory.not-found", ("path", string.Create(CultureInfo.InvariantCulture, $"/media/Movies/Film {number}.mkv")));

    /// <summary>One more row than a page holds and one after it, each an older build's English, in rowid order.</summary>
    private async Task<List<(long RowId, string English)>> PlantTwoPagesAsync(CancellationToken cancellationToken)
    {
        var rows = new List<(long RowId, string English)>();
        for (var number = 0; number < StoredTextConverter.PageSize + 2; number++)
        {
            var english = TestTexts.En(NotFound(number))!;
            rows.Add((await PlantAsync("PhysicalFiles", "InspectionError", english, cancellationToken), english));
        }

        return rows;
    }

    /// <summary>The converter's write of row <paramref name="rowId"/> throws, once — a full disk, a file another process
    /// locked.</summary>
    private void FailTheWriteOf(long rowId) =>
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.StartsWith("UPDATE \"PhysicalFiles\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, rowId)),
            _ => throw new InvalidOperationException("The disk went away."));

    /// <summary>Every file's inspection error as the column reads it — a message, or an older build's words — in
    /// English.</summary>
    private async Task<List<string>> InspectionErrorsAsync(CancellationToken cancellationToken)
    {
        var english = new List<string>();
        await _factory.WithDbAsync(async db =>
            english.AddRange((await db.PhysicalFiles.Select(file => file.InspectionError).ToListAsync(cancellationToken)).Select(error => TestTexts.En(error)!)));
        return english;
    }

    /// <summary>One run of the startup converter, in a scope of its own, as the start resolves it.</summary>
    private StoredTextConversionResult Convert()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<StoredTextConverter>().Convert();
    }

    /// <summary>
    /// Every column of the database a <see cref="Message"/> property maps to, as <c>Table.Column</c>.
    /// </summary>
    /// <remarks>MAM-47 WP10 review — read from the relational model (the tables and their columns, whatever maps a
    /// property there: an inherited, owned or complex type's too), not by the converter's own walk over the entity types,
    /// so a column that walk would miss fails the guard instead of passing it by the same blind spot.</remarks>
    private static IEnumerable<string> MessageColumnsOfTheModel()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=:memory:").Options);
        return
        [
            .. from table in db.Model.GetRelationalModel().Tables
               from column in table.Columns
               where column.PropertyMappings.Any(mapping => mapping.Property.ClrType == typeof(Message))
               select $"{table.Name}.{column.Name}",
        ];
    }

    /// <summary>
    /// Plants one row of <paramref name="table"/> holding <paramref name="words"/> in <paramref name="column"/>, as an
    /// older build left it: raw text, through SQL. Every other required column gets a filler and foreign keys are off, so
    /// the row needs no parent — the converter reads only the column — and its <c>rowid</c> comes back.
    /// </summary>
    private async Task<long> PlantAsync(
        string table, string column, string words, CancellationToken cancellationToken, params (string Column, object Value)[] values)
    {
        long rowId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var row = FillersOf(db.Model, table);
            row[column] = words;
            foreach (var (name, value) in values)
                row[name] = value;
            var names = row.Keys.ToList();
            var insert = $"PRAGMA foreign_keys = OFF; INSERT INTO {Quote(table)} ({string.Join(", ", names.Select(Quote))}) " +
                $"VALUES ({string.Join(", ", names.Select((_, index) => $"{{{index}}}"))}); PRAGMA foreign_keys = ON;";
            await db.Database.ExecuteSqlRawAsync(insert, names.Select(name => row[name]), cancellationToken);
            var select = $"SELECT max(rowid) AS \"Value\" FROM {Quote(table)}";
            rowId = await db.Database.SqlQueryRaw<long>(select).SingleAsync(cancellationToken);
        });
        return rowId;
    }

    private Task<long> PlantAuditNameAsync(AuditAction action, string name, CancellationToken cancellationToken) =>
        PlantAsync("AuditEvents", "TargetName", name, cancellationToken, ("Action", action.ToString()));

    private Task SetLinkEnglishAsync(Guid linkId, string english, CancellationToken cancellationToken) =>
        _factory.WithDbAsync(db => db.Database.ExecuteSqlAsync($"""UPDATE "LibraryItemLinks" SET "WriteError" = {english} WHERE "Id" = {linkId}""", cancellationToken));

    private async Task<string> StoredAsync(string table, string column, long rowId, CancellationToken cancellationToken)
    {
        var text = "";
        await _factory.WithDbAsync(async db =>
        {
            var select = $"SELECT {Quote(column)} AS \"Value\" FROM {Quote(table)} WHERE rowid = {{0}}";
            text = await db.Database.SqlQueryRaw<string>(select, rowId).SingleAsync(cancellationToken);
        });
        return text;
    }

    /// <summary>Every message column's text and every audit name, by table, column and row; a null as <c>NULL</c>, a text
    /// quoted, so a null that became an empty text (or the other way round) shows.</summary>
    private async Task<List<string>> SnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = new List<string>();
        foreach (var column in MessageColumnsOfTheModel().Order(StringComparer.Ordinal).Append("AuditEvents.TargetName"))
        {
            var (table, name) = (column[..column.IndexOf('.', StringComparison.Ordinal)], column[(column.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            await _factory.WithDbAsync(async db =>
            {
                var select = $"SELECT '{column} ' || rowid || ' ' || quote({Quote(name)}) AS \"Value\" FROM {Quote(table)} ORDER BY rowid";
                snapshot.AddRange(await db.Database.SqlQueryRaw<string>(select).ToListAsync(cancellationToken));
            });
        }

        return snapshot;
    }

    /// <summary>A value for every column of <paramref name="table"/> that can't be null, by its type in the database;
    /// strings and ids unique, so no unique index of the table refuses a second row.</summary>
    private static Dictionary<string, object> FillersOf(IModel model, string table)
    {
        var row = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in model.GetEntityTypes().Where(type => type.GetTableName() == table).SelectMany(type => type.GetProperties()))
        {
            if (!property.IsNullable)
                row.TryAdd(property.GetColumnName(), FillerOf(property));
        }

        return row;
    }

    private static object FillerOf(IProperty property)
    {
        var type = property.GetTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(string) ? Guid.NewGuid().ToString("N")
            : type == typeof(Guid) ? Guid.NewGuid()
            : type == typeof(bool) ? false
            : type == typeof(DateTime) ? DateTime.UtcNow
            : type == typeof(DateTimeOffset) ? DateTimeOffset.UtcNow
            : type == typeof(TimeSpan) ? TimeSpan.Zero
            : type == typeof(byte[]) ? new byte[] { 0 }
            : type.IsEnum ? Enum.GetValues(type).GetValue(0)!
            : System.Convert.ChangeType(Random.Shared.Next(1, 120), type, CultureInfo.InvariantCulture);
    }

    private static string Quote(string name) => "\"" + name + "\"";
}
