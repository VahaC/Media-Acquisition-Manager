using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-47 (ADR 0042) — a Message property is the TEXT column it was, holding key + values as canonical JSON, fitted into
// its 1000 characters, and English an older build stored reads back as its words (Tolerant Reader). Over real SQLite,
// through a context that inherits ApplicationDbContext's own conventions and adds one probe column — the app's columns
// are typed by the packages that convert them.
public sealed class MessageColumnTests : DatabaseTestBase
{
    private const string SelectReason = "SELECT \"Reason\" AS \"Value\" FROM \"MessageProbes\" WHERE \"Id\" = {0}";

    private static readonly Message Reason = Message.Of(
        "files.quarantine.os-failed",
        ("path", "/media/Фільми/Alien (1979).mkv"),
        ("reason", VendorText.Clean("Device or resource busy", 300)));

    [Fact]
    public async Task SaveChanges_stores_a_message_as_its_canonical_json_and_reads_it_back_equal()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = await CreateProbeContextAsync(cancellationToken))
        {
            db.Probes.Add(new ProbeEntity { Id = 1, Reason = Reason });

            //Act
            await db.SaveChangesAsync(cancellationToken);
        }

        //Assert
        await using var check = CreateProbeContext();
        var stored = await check.Database.SqlQueryRaw<string>(SelectReason, 1).SingleAsync(cancellationToken);
        Assert.Equal(
            "{\"k\":\"files.quarantine.os-failed\",\"v\":{\"path\":\"/media/Фільми/Alien (1979).mkv\",\"reason\":{\"vendor\":\"Device or resource busy\"}}}",
            stored);
        var row = await check.Probes.AsNoTracking().SingleAsync(probe => probe.Id == 1, cancellationToken);
        Assert.Equal(Reason, row.Reason);
    }

    [Fact]
    public async Task SaveChanges_fits_a_message_longer_than_the_column_by_cutting_its_longest_value()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var longPath = "/media/" + new string('a', 2_000);
        var message = Message.Of("files.sample", ("path", longPath), ("name", "Alien (1979).mkv"));
        await using (var db = await CreateProbeContextAsync(cancellationToken))
        {
            db.Probes.Add(new ProbeEntity { Id = 1, Reason = message });

            //Act
            await db.SaveChangesAsync(cancellationToken);
        }

        //Assert
        await using var check = CreateProbeContext();
        var stored = await check.Database.SqlQueryRaw<string>(SelectReason, 1).SingleAsync(cancellationToken);
        Assert.Equal(MessageJson.ColumnLength, stored.Length);
        var row = await check.Probes.AsNoTracking().SingleAsync(probe => probe.Id == 1, cancellationToken);
        Assert.Equal(MessageFit.Fit(message, MessageJson.ColumnLength), row.Reason);
        Assert.Equal(new TextValue("Alien (1979).mkv"), row.Reason!.Values["name"]);
    }

    [Theory]
    [InlineData("Couldn't reach “Transmission”: Connection refused.")]
    [InlineData("{\"k\": broken")]
    [InlineData("{\"k\":\"Not A Key\"}")]
    public async Task A_column_holding_no_message_json_reads_as_its_words(string stored)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = await CreateProbeContextAsync(cancellationToken))
            await db.Database.ExecuteSqlRawAsync("INSERT INTO \"MessageProbes\" (\"Id\", \"Reason\") VALUES (1, {0})", [stored], cancellationToken);

        //Act
        await using var check = CreateProbeContext();
        var row = await check.Probes.AsNoTracking().SingleAsync(probe => probe.Id == 1, cancellationToken);

        //Assert
        Assert.Equal(Message.Legacy(stored), row.Reason);
    }

    [Fact]
    public async Task A_null_column_stays_null_and_filters_as_before()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = await CreateProbeContextAsync(cancellationToken))
        {
            db.Probes.AddRange(new ProbeEntity { Id = 1, Reason = null }, new ProbeEntity { Id = 2, Reason = Reason });
            await db.SaveChangesAsync(cancellationToken);
        }

        //Act
        await using var check = CreateProbeContext();
        var withReason = await check.Probes.AsNoTracking().Where(probe => probe.Reason != null).Select(probe => probe.Id).ToListAsync(cancellationToken);
        var withoutReason = await check.Probes.AsNoTracking().SingleAsync(probe => probe.Reason == null, cancellationToken);

        //Assert
        Assert.Equal([2], withReason);
        Assert.Equal(1, withoutReason.Id);
        Assert.Null(withoutReason.Reason);
    }

    // MAM-47 — EF's change tracking compares a column's value with Message.Equals: a message built anew with the same key
    // and values is no change, so a writer that sets the same reason again (the poller every two seconds) writes nothing.
    [Fact]
    public async Task Setting_an_equal_message_built_anew_is_no_change()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = await CreateProbeContextAsync(cancellationToken))
        {
            db.Probes.Add(new ProbeEntity { Id = 1, Reason = Reason });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using var check = CreateProbeContext();
        var row = await check.Probes.SingleAsync(probe => probe.Id == 1, cancellationToken);

        //Act
        row.Reason = Message.Of(
            "files.quarantine.os-failed",
            ("reason", VendorText.Clean("Device or resource busy", 300)),
            ("path", "/media/Фільми/Alien (1979).mkv"));
        var unchanged = check.ChangeTracker.HasChanges();
        row.Reason = row.Reason.With("path", "/media/other.mkv");
        var changed = check.ChangeTracker.HasChanges();

        //Assert
        Assert.False(unchanged);
        Assert.True(changed);
    }

    // MAM-47 — how LibraryLinkService's catch-up will select rows by a value-less message (§5.4): EF converts the constant
    // through the same converter, so the comparison is on the canonical JSON, with today's null semantics.
    [Fact]
    public async Task Where_compares_a_column_with_a_message_constant_through_the_converter()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var restarted = Message.Of("library-links.write.restarted");
        await using (var db = await CreateProbeContextAsync(cancellationToken))
        {
            db.Probes.AddRange(
                new ProbeEntity { Id = 1, Reason = restarted },
                new ProbeEntity { Id = 2, Reason = Reason },
                new ProbeEntity { Id = 3, Reason = null });
            await db.SaveChangesAsync(cancellationToken);
        }

        //Act
        await using var check = CreateProbeContext();
        var notRestarted = await check.Probes.AsNoTracking()
            .Where(probe => probe.Reason != restarted)
            .Select(probe => probe.Id)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

        //Assert
        Assert.Equal([2, 3], notRestarted);
    }

    private ProbeContext CreateProbeContext() => new(CreateOptions());

    private async Task<ProbeContext> CreateProbeContextAsync(CancellationToken cancellationToken)
    {
        var db = CreateProbeContext();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        return db;
    }

    private DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMediaAcquisitionManagerSqlite(DatabasePath)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

    /// <summary>The app's context with one probe column, typed <see cref="Message"/> as the app's will be.</summary>
    private sealed class ProbeContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public DbSet<ProbeEntity> Probes => Set<ProbeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ProbeEntity>(probe =>
            {
                probe.ToTable("MessageProbes");
                probe.Property(entity => entity.Id).ValueGeneratedNever();
            });
        }
    }

    private sealed class ProbeEntity
    {
        public int Id { get; set; }

        [MaxLength(MessageJson.ColumnLength)]
        public Message? Reason { get; set; }
    }
}
