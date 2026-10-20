using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-47 (ADR 0042, design §6.8) — the start after an upgrade from a build that stored sentences in English: before the
/// host opens its database, the copy holds an owner, two of his download jobs whose reason and block reason are English,
/// and two links of a library that identifies items, waiting with an older build's English — one the restart sentence the
/// links' repair must leave alone, one another reason it may take. The host's own start (the migrations, then the startup
/// converter) meets them — and the read of the first column it walks, <see cref="FailingColumn"/>, throws once (a full
/// disk, a file another process locked), so the start shows the converter logs it, goes on with the others and never
/// stops the boot.
/// </summary>
/// <remarks>
/// Planted with the data factories over a context of its own, through the real encryption with the host's
/// <see cref="MediaAcquisitionManagerApiFactory.TestEncryptionKey"/> — so the host reads the rows back as its own — and
/// the sentences then written over the message columns as raw text, the way an older build left them. A test class on
/// this factory must not call <see cref="MediaAcquisitionManagerApiFactory.ResetAsync"/>: it would empty the rows that
/// are the subject.
/// </remarks>
public sealed class StoredEnglishApiFactory : MediaAcquisitionManagerApiFactory
{
    /// <summary>The fake library's name, which the switched-off reason names.</summary>
    public const string LibraryName = "Attic";

    /// <summary>The download client the jobs name.</summary>
    public const string ClientName = "qBittorrent";

    /// <summary>The message column whose read throws at start: the first the converter walks (ordinal order), so the
    /// planted columns come after it.</summary>
    public const string FailingColumn = "CopyRemovalNames.Problem";

    /// <summary>What the failing read throws.</summary>
    public const string FailingReadError = "The disk went away.";

    /// <summary>The failed job's reason: a feed's refusal, with the feed's own words nested in it.</summary>
    public static readonly Message FailedReason = Message.Of(
        "torznab.download.failed",
        ("error", Message.Of("torznab.error.described-clause", ("code", 500), ("description", VendorText.Clean("Download failed", 300)))));

    /// <summary>The followed job's block reason.</summary>
    public static readonly Message MissingReason = Message.Of("downloads.follow.missing", ("client", ClientName));

    /// <summary>The restart sentence the links' reconciler stores: the repair leaves such a link to the owner's Retry.</summary>
    public static readonly Message RestartedReason = Message.Of("library-links.write.restarted");

    /// <summary>Another reason a link waits with, which the repair takes.</summary>
    public static readonly Message SwitchedOffReason = Message.Of("library-links.write.switched-off", ("integration", LibraryName));

    /// <summary>The owner, who requested both jobs.</summary>
    public UserEntity Owner { get; private set; } = null!;

    /// <summary>The job that failed, with <see cref="FailedReason"/> as its English error.</summary>
    public Guid FailedJobId { get; private set; }

    /// <summary>The job still followed, with <see cref="MissingReason"/> as its English block reason.</summary>
    public Guid MissingJobId { get; private set; }

    /// <summary>The link waiting with <see cref="RestartedReason"/> in English.</summary>
    public Guid RestartedLinkId { get; private set; }

    /// <summary>The link waiting with <see cref="SwitchedOffReason"/> in English.</summary>
    public Guid SwitchedOffLinkId { get; private set; }

    /// <summary>
    /// The four planted columns — the failed job's error, the followed job's block reason, the restarted link's and the
    /// switched-off link's write error — as the host's start left them, read the moment it had started, before any client
    /// existed to send a request.
    /// </summary>
    public IReadOnlyList<string> StoredAtStart { get; private set; } = [];

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // With minimal hosting the app's Main runs inside this call up to app.Run(): the migrations and the startup
        // converter have run when it returns, and the test client is built only afterwards.
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        StoredAtStart =
        [
            db.Database.SqlQuery<string>($"""SELECT "Error" AS "Value" FROM "DownloadJobs" WHERE "Id" = {FailedJobId}""").AsEnumerable().Single(),
            db.Database.SqlQuery<string>($"""SELECT "BlockReason" AS "Value" FROM "DownloadJobs" WHERE "Id" = {MissingJobId}""").AsEnumerable().Single(),
            db.Database.SqlQuery<string>($"""SELECT "WriteError" AS "Value" FROM "LibraryItemLinks" WHERE "Id" = {RestartedLinkId}""").AsEnumerable().Single(),
            db.Database.SqlQuery<string>($"""SELECT "WriteError" AS "Value" FROM "LibraryItemLinks" WHERE "Id" = {SwitchedOffLinkId}""").AsEnumerable().Single(),
        ];
        return host;
    }

    protected override void OnDatabaseCopied(string databasePath)
    {
        // Off the test's synchronization context: the data factories await EF, and the host's configuration that calls
        // this is synchronous.
        Task.Run(() => PlantAsync(databasePath)).GetAwaiter().GetResult();
        // Armed on the host's own commands (the hook is on every context it makes); nothing before the converter reads
        // that table, and the hook runs once.
        var table = FailingColumn[..FailingColumn.IndexOf('.', StringComparison.Ordinal)];
        DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException(FailingReadError));
    }

    private async Task PlantAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseMediaAcquisitionManagerSqlite(databasePath).Options;
        await using var db = new ApplicationDbContext(options);
        var encryption = new AesGcmEncryptionService(Options.Create(new EncryptionOptions { Key = TestEncryptionKey }));

        Owner = await new UserDataFactory(db, PasswordHasher, Time).Create("owner", UserSeeder.DefaultPassword);
        await new InstallationDataFactory(db, Time).SetOwner(Owner.Id);

        var releases = new ReleaseDataFactory(db, encryption, Time);
        var jobs = new DownloadJobDataFactory(db, Time);
        var failedRelease = await releases.Create(sourceConnectionId: null, title: "Alien (1979) 2160p");
        FailedJobId = (await jobs.Create(failedRelease.Id, null, ClientName, status: DownloadJobStatus.Failed, requestedByUserId: Owner.Id)).Id;
        var missingRelease = await releases.Create(sourceConnectionId: null, title: "Aliens (1986) 1080p");
        MissingJobId = (await jobs.Create(missingRelease.Id, null, ClientName, status: DownloadJobStatus.Downloading, requestedByUserId: Owner.Id)).Id;

        var library = await new IntegrationConnectionDataFactory(db, encryption, Time).Create(LibraryName, FakeLibraryItemIdentifier.TypeId, IntegrationKind.MediaLibrary);
        var work = await new MediaWorkDataFactory(db, Time).Create();
        var links = new LibraryItemLinkDataFactory(db, Time);
        RestartedLinkId = (await links.Create(library.Id, "item-restarted", work.Id, MediaIdentifierScheme.Tmdb, "348", writeState: LibraryLinkWriteState.NotWritten)).Id;
        SwitchedOffLinkId = (await links.Create(library.Id, "item-switched-off", work.Id, MediaIdentifierScheme.Tmdb, "679", writeState: LibraryLinkWriteState.NotWritten)).Id;

        // The words an older build stored, written over the columns as they were: raw text, not a message.
        await db.Database.ExecuteSqlAsync($"""UPDATE "DownloadJobs" SET "Error" = {TestTexts.En(FailedReason)} WHERE "Id" = {FailedJobId}""");
        await db.Database.ExecuteSqlAsync($"""UPDATE "DownloadJobs" SET "BlockReason" = {TestTexts.En(MissingReason)} WHERE "Id" = {MissingJobId}""");
        await db.Database.ExecuteSqlAsync($"""UPDATE "LibraryItemLinks" SET "WriteError" = {TestTexts.En(RestartedReason)} WHERE "Id" = {RestartedLinkId}""");
        await db.Database.ExecuteSqlAsync($"""UPDATE "LibraryItemLinks" SET "WriteError" = {TestTexts.En(SwitchedOffReason)} WHERE "Id" = {SwitchedOffLinkId}""");

        // Closed for good before the host opens the file.
        SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
    }
}
