using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-13 — which of a client's jobs a submission became, by the info hashes worked out from its payload
// (specification §3.2, step 6): one job holding one of them is the submission's, none is none, and more
// than one distinct job is left to link by hand rather than guessed. A job's name never counts.
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

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ClientJob Job(string id, IReadOnlyList<string> hashes, string name = "Interstellar (2014)") =>
        new(id, name, ClientJobState.Downloading, 0.5, "Movies", "/downloads/movies/x.mkv", hashes);
}
