using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-13 — which of a client's jobs a submission became, by the info hashes worked out from its payload
// (specification §3.2, step 6): one job holding one of them is the submission's, none is none, and more
// than one distinct job is left to link by hand rather than guessed. A job's name never counts. MAM-14 — a
// followed download is found the same way, by the client's id stored on it as well as by its hashes, and is
// looked up by those of them that are info hashes.
public sealed class ClientJobCorrelationTests
{
    private const string V1 = "72d6069eff259a0800f7afdb2500d5de591a9f01";
    private const string V2 = "3d8c42fa5353329627f1a2afcba8bc04c6b5db714d3b07577c9624e6a4396bc2";
    private const string Other = "1ec461458feffe9f27ae17e30d0a804e2ab0a88f";

    [Fact]
    public void Match_finds_the_one_job_holding_the_v1_hash()
    {
        //Arrange
        var jobs = new[] { Job(Other, [Other]), Job(V1, [V1]) };

        //Act
        var match = ClientJobCorrelation.Match(jobs, new TorrentInfoHashes(V1, null));

        //Assert
        Assert.Equal(ClientJobMatchStatus.Found, match.Status);
        Assert.Equal(V1, match.Job!.ClientJobId);
    }

    [Fact]
    public void Match_finds_a_job_by_its_v2_hash_whatever_the_client_calls_it()
    {
        //Arrange
        // A v2 torrent's id in a client can be its hash cut short; the full v2 hash it lists still matches.
        var jobs = new[] { Job(V2[..40], [V2[..40], V2]) };

        //Act
        var match = ClientJobCorrelation.Match(jobs, new TorrentInfoHashes(null, V2));

        //Assert
        Assert.Equal(ClientJobMatch.Found(jobs[0]), match);
    }

    [Fact]
    public void Match_compares_hashes_in_any_letter_case()
    {
        //Arrange
        var jobs = new[] { Job("JOB-1", [V1.ToUpperInvariant()]) };

        //Act
        var match = ClientJobCorrelation.Match(jobs, new TorrentInfoHashes(V1, null));

        //Assert
        Assert.Equal(ClientJobMatchStatus.Found, match.Status);
    }

    [Fact]
    public void Match_without_a_job_holding_the_hashes_is_none()
    {
        //Arrange
        var jobs = new[] { Job(Other, [Other]) };

        //Act
        var match = ClientJobCorrelation.Match(jobs, new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal(ClientJobMatch.None, match);
    }

    [Fact]
    public void Match_without_any_known_hash_is_none_even_when_a_name_would_match()
    {
        //Arrange
        var jobs = new[] { Job(V1, [V1], name: "x.mkv") };

        //Act
        var match = ClientJobCorrelation.Match(jobs, TorrentInfoHashes.None);

        //Assert
        Assert.Equal(ClientJobMatch.None, match);
    }

    [Fact]
    public void Match_of_two_distinct_jobs_holding_the_hashes_is_ambiguous()
    {
        //Arrange
        // A hybrid torrent's v1 in one job and its v2 in another: which one is the submission's is not
        // the app's to guess.
        var jobs = new[] { Job("JOB-1", [V1]), Job("JOB-2", [V2]) };

        //Act
        var match = ClientJobCorrelation.Match(jobs, new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal(ClientJobMatch.Ambiguous, match);
    }

    [Fact]
    public void Match_counts_a_job_listed_twice_under_one_id_once()
    {
        //Arrange
        var jobs = new[] { Job(V1, [V1]), Job(V1, [V1]) };

        //Act
        var match = ClientJobCorrelation.Match(jobs, new TorrentInfoHashes(V1, null));

        //Assert
        Assert.Equal(ClientJobMatchStatus.Found, match.Status);
    }

    [Fact]
    public void Match_of_no_jobs_is_none()
    {
        //Arrange

        //Act
        var match = ClientJobCorrelation.Match([], new TorrentInfoHashes(V1, null));

        //Assert
        Assert.Equal(ClientJobMatch.None, match);
    }

    [Fact]
    public void Follow_finds_the_job_by_its_stored_id_without_any_hash()
    {
        //Arrange
        // MAM-13's "taken without an id" is the other way round; a job confirmed by the client's answer may
        // have an id and no hash the client lists.
        var jobs = new[] { Job(Other, [Other]), Job(V1, [V1]) };

        //Act
        var match = ClientJobCorrelation.Follow(jobs, V1, TorrentInfoHashes.None);

        //Assert
        Assert.Equal(ClientJobMatch.Found(jobs[1]), match);
    }

    [Fact]
    public void Follow_finds_a_job_the_client_renamed_by_the_stored_id_among_its_hashes()
    {
        //Arrange
        // The ADR 0013 rename: a hybrid sent as a magnet naming its v1 hash, renamed by qBittorrent on
        // libtorrent 2 to its v2 hash cut to 40 characters once the metadata arrived; it still lists the v1.
        var jobs = new[] { Job(V2[..40], [V2[..40], V1, V2]) };

        //Act
        var match = ClientJobCorrelation.Follow(jobs, V1, TorrentInfoHashes.None);

        //Assert
        Assert.Equal(ClientJobMatch.Found(jobs[0]), match);
    }

    [Fact]
    public void Follow_finds_a_job_taken_without_an_id_by_either_hash()
    {
        //Arrange
        var jobs = new[] { Job(Other, [Other]), Job(V2[..40], [V2[..40], V2]) };

        //Act
        var match = ClientJobCorrelation.Follow(jobs, null, new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal(ClientJobMatch.Found(jobs[1]), match);
    }

    [Fact]
    public void Follow_compares_the_id_exactly_and_hashes_in_any_letter_case()
    {
        //Arrange
        // An id that isn't a hash (another client's own) is the client's to spell; a hash is hexadecimal.
        var jobs = new[] { Job("nzo_ab12cd", []), Job("JOB-2", [V1.ToUpperInvariant()]) };

        //Act
        var byOtherCase = ClientJobCorrelation.Follow(jobs, "NZO_AB12CD", TorrentInfoHashes.None);
        var byHashInList = ClientJobCorrelation.Follow(jobs, V1, TorrentInfoHashes.None);

        //Assert
        Assert.Equal(ClientJobMatch.None, byOtherCase);
        Assert.Equal(ClientJobMatch.Found(jobs[1]), byHashInList);
    }

    [Fact]
    public void Follow_without_an_id_or_a_hash_is_none_even_when_a_name_would_match()
    {
        //Arrange
        var jobs = new[] { Job(V1, [V1], name: "x.mkv") };

        //Act
        var withNull = ClientJobCorrelation.Follow(jobs, null, TorrentInfoHashes.None);
        var withEmpty = ClientJobCorrelation.Follow(jobs, "", TorrentInfoHashes.None);

        //Assert
        Assert.Equal(ClientJobMatch.None, withNull);
        Assert.Equal(ClientJobMatch.None, withEmpty);
    }

    [Fact]
    public void Follow_of_a_job_the_client_no_longer_lists_is_none()
    {
        //Arrange
        var jobs = new[] { Job(Other, [Other]) };

        //Act
        var match = ClientJobCorrelation.Follow(jobs, V1, new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal(ClientJobMatch.None, match);
    }

    [Fact]
    public void Follow_of_one_job_by_its_id_and_another_by_a_hash_is_ambiguous()
    {
        //Arrange
        var jobs = new[] { Job(V1, [V1]), Job("JOB-2", [V2]) };

        //Act
        var match = ClientJobCorrelation.Follow(jobs, V1, new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal(ClientJobMatch.Ambiguous, match);
    }

    [Fact]
    public void Follow_counts_a_job_matched_by_its_id_and_its_hashes_once()
    {
        //Arrange
        var jobs = new[] { Job(V2[..40], [V2[..40], V1, V2]), Job(V2[..40], [V2[..40], V1, V2]) };

        //Act
        var match = ClientJobCorrelation.Follow(jobs, V2[..40], new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal(ClientJobMatchStatus.Found, match.Status);
    }

    [Fact]
    public void FollowIds_are_the_id_and_both_hashes_lower_case_each_once()
    {
        //Arrange

        //Act
        var ids = ClientJobCorrelation.FollowIds(V1.ToUpperInvariant(), new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal([V1, V2], ids);
    }

    [Fact]
    public void FollowIds_keep_the_renamed_id_and_the_hashes_it_was_sent_with()
    {
        //Arrange

        //Act
        var ids = ClientJobCorrelation.FollowIds(V2[..40], new TorrentInfoHashes(V1, V2));

        //Assert
        Assert.Equal([V2[..40], V1, V2], ids);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nzo_ab12cd")]
    [InlineData("72d6069eff259a0800f7afdb2500d5de591a9f0")]
    [InlineData("72d6069eff259a0800f7afdb2500d5de591a9f01 ")]
    [InlineData("72d6069eff259a0800f7afdb2500d5de591a9f0g")]
    [InlineData("١2d6069eff259a0800f7afdb2500d5de591a9f01")]
    public void FollowIds_leave_out_an_id_that_is_not_an_info_hash(string? clientJobId)
    {
        //Arrange
        // The client contract throws for one, and one bad row must not stop its client's whole lookup.

        //Act
        var ids = ClientJobCorrelation.FollowIds(clientJobId, new TorrentInfoHashes(V1, null));

        //Assert
        Assert.Equal([V1], ids);
    }

    [Fact]
    public void FollowIds_without_an_id_or_a_hash_are_none()
    {
        //Arrange

        //Act
        var ids = ClientJobCorrelation.FollowIds(null, TorrentInfoHashes.None);

        //Assert
        Assert.Empty(ids);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ClientJob Job(string id, IReadOnlyList<string> hashes, string name = "Interstellar (2014)") =>
        new(id, name, ClientJobState.Downloading, 0.5, "Movies", "/downloads/movies/x.mkv", hashes);
}
