using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — whether a replacement's new file is still the file its check read (ADR 0035): the same device and inode, size
/// and modification time at its path. MAM-26 — one rule for both readers of it: the verifier, on every pass of the
/// library's stage, and the mover, right before it moves the old copy the new file was paired with (check 5), so the two
/// can't tell a changed file apart differently.
/// </summary>
internal static class ReplacementNewFileCheck
{
    /// <summary>
    /// Why the new file of <paramref name="row"/> is no longer the file its check read, looked at inside
    /// <paramref name="roots"/> through <paramref name="files"/> — gone, unreadable, or another identity, size or
    /// modification time —; <c>null</c> when it still is. Only the cheap look at its identity: the media inspector read it
    /// already. The sentence is the verifier's — MAM-47: a message, the disk's own when it gave one —; the mover words its
    /// own stop.
    /// </summary>
    public static async Task<Message?> ChangedAsync(
        IFileInventory files, ReplacementJobNewFileEntity row, IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        var path = MessageText.Clean(row.AppPath);
        var described = await files.DescribeAsync(row.AppPath!, roots, cancellationToken);
        if (described is not { Status: FileInventoryStatus.Found, Value: { } facts })
            return described.Error ?? Message.Of("replacements.new-files.unread-again", ("path", path));
        return facts.DeviceId == row.DeviceId && facts.Inode == row.Inode && facts.SizeBytes == row.SizeBytes && facts.ModifiedUtc == row.ModifiedUtc
            ? null
            : Message.Of("replacements.new-files.changed", ("path", path));
    }
}
