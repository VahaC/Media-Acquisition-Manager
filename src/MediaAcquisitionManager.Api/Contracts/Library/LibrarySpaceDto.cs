namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// How much room a set of file names takes on disk (MAM-19, ADR 0018): each file counted once, by its device
/// and inode, however many names lead to it, so two hardlinks of one file take its size once.
/// </summary>
/// <param name="SizeBytes">The sizes of the files the names lead to, each file once, as the app's last look
/// at each read them.</param>
/// <param name="FileCount">How many files that is.</param>
/// <param name="NameCount">How many of the names lead to them: more than <paramref name="FileCount"/> when a
/// file has several names.</param>
/// <param name="UnmeasuredCount">How many names the app knows no file behind: not looked at yet, or its last
/// look found nothing there or no identity. Left out of <paramref name="SizeBytes"/>.</param>
public sealed record LibrarySpaceDto(long SizeBytes, int FileCount, int NameCount, int UnmeasuredCount);
