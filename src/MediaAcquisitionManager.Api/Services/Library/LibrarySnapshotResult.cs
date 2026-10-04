namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>The outcome of <see cref="ILibrarySnapshotStore.ApplyAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="WorksCount">How many movies and episodes the stored snapshot holds as works; 0 unless
/// applied.</param>
/// <param name="SeriesCount">How many series it holds (MAM-32); 0 unless applied.</param>
/// <param name="VersionsCount">How many versions it holds; 0 unless applied.</param>
/// <param name="FilesCount">How many file names it holds; 0 unless applied.</param>
public sealed record LibrarySnapshotResult(LibrarySnapshotStatus Status, int WorksCount, int SeriesCount, int VersionsCount, int FilesCount)
{
    public static LibrarySnapshotResult SourceRemoved { get; } = new(LibrarySnapshotStatus.SourceRemoved, 0, 0, 0, 0);

    public static LibrarySnapshotResult RunNoLongerRunning { get; } = new(LibrarySnapshotStatus.RunNoLongerRunning, 0, 0, 0, 0);

    public static LibrarySnapshotResult Applied(int worksCount, int seriesCount, int versionsCount, int filesCount) =>
        new(LibrarySnapshotStatus.Applied, worksCount, seriesCount, versionsCount, filesCount);
}
