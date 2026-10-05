namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>The hashing step's columns of a planted file inspection run (MAM-19), stored as given.</summary>
/// <param name="Candidates">The files it listed as needing a hash; <c>null</c> before it listed them.</param>
/// <param name="Checked">How many of them it dealt with.</param>
/// <param name="Read">How many it read in full.</param>
/// <param name="ProblemCount">How many it couldn't hash.</param>
/// <param name="Problem">The first one's sentence.</param>
public sealed record PlantedHashing(int? Candidates, int Checked = 0, int Read = 0, int ProblemCount = 0, string? Problem = null);
