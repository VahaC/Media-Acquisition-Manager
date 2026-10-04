namespace MediaAcquisitionManager.Core.Matching;

/// <summary>
/// The years a work's title may be of to go with the titles of a <see cref="WorkClaim"/> (MAM-32): from
/// <paramref name="Earliest"/> to <paramref name="Latest"/>, both included.
/// </summary>
/// <remarks>
/// A search and a movie release name one year, which the work's must equal (<see cref="Exactly"/>). A TV
/// release names the years of the seasons it holds, never the show's own (ADR 0020), so what its year
/// says of the show depends on those seasons: one year or a span, or, for a pack of later seasons, only
/// the latest year the show can have begun in — which a <c>null</c> <paramref name="Earliest"/> stands for.
/// <c>ReleaseWorkClaims.YearsToMatch</c> picks which.
/// </remarks>
/// <param name="Earliest">The earliest year that goes with the titles; <c>null</c> for any year up to
/// <paramref name="Latest"/>.</param>
/// <param name="Latest">The latest year that goes with the titles; never before
/// <paramref name="Earliest"/>.</param>
public sealed record ClaimedYears(int? Earliest, int Latest)
{
    /// <summary><paramref name="year"/> alone; <c>null</c>, no year to go with the titles, when it is
    /// <c>null</c>.</summary>
    public static ClaimedYears? Exactly(int? year) => year is { } known ? new ClaimedYears(known, known) : null;

    /// <summary>Whether a title of <paramref name="year"/> goes with the claim's titles; a title of no
    /// year never does.</summary>
    public bool Holds(int? year) =>
        year is { } known && (Earliest is not { } earliest || known >= earliest) && known <= Latest;
}
