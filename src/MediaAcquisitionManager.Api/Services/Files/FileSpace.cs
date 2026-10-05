using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// How much room a set of file names takes on disk (MAM-19, ADR 0018), counted from the names when asked, so
/// it moves as an inspection writes them. One rule for a library's row, the Media folders row and the whole
/// library on the Library page, so the three count alike.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0018 — a file's size counts once per device and inode: the names of one file (hardlinks) take its
/// size once, whether they come from one library, two, or a library and the scan. A file a torrent seeds is
/// counted like any other; no download client is asked.
/// </para>
/// <para>
/// Only what the app read itself counts (ADR 0006): the identity and the size its last look at the name read
/// (<c>DeviceId</c>, <c>Inode</c>, <c>ReadSizeBytes</c>), never the size a library reported, which says
/// nothing about whether two names share their bytes. A name it has no such look for is counted apart, as
/// not measured, never guessed into the total.
/// </para>
/// </remarks>
public static class FileSpace
{
    /// <summary>The room the files behind <paramref name="names"/> take, each file once.</summary>
    /// <param name="names">Each name's identity and size as the app's last look read them; any of them
    /// <c>null</c> when it has none.</param>
    public static LibrarySpaceDto Of(IEnumerable<(long? DeviceId, long? Inode, long? SizeBytes)> names)
    {
        var files = new Dictionary<(long DeviceId, long Inode), long>();
        var nameCount = 0;
        var unmeasuredCount = 0;
        foreach (var (deviceId, inode, sizeBytes) in names)
        {
            if (deviceId is not { } device || inode is not { } node || sizeBytes is not { } size)
            {
                unmeasuredCount++;
                continue;
            }

            nameCount++;
            // Two names of one file looked at a moment apart can disagree when it was written in between; the
            // larger size stands, as the file never takes both.
            files[(device, node)] = Math.Max(size, files.GetValueOrDefault((device, node)));
        }

        return new LibrarySpaceDto(files.Values.Sum(), files.Count, nameCount, unmeasuredCount);
    }
}
