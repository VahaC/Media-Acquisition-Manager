using System.Globalization;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// MAM-20, ADR 0028 — Linux's mount table (<c>/proc/self/mountinfo</c>) read for the type of file system a device
/// holds: what decides whether removing a byte-identical copy frees its room. Pure: the caller reads the table.
/// </summary>
/// <remarks>
/// <para>
/// A line is <c>id parent major:minor root mount-point options [optional fields…] - type source super-options</c>
/// (proc(5)). The optional fields come and go, so the type is found after the lone <c>-</c> that ends them, never at a
/// fixed place; a space in a path is written <c>\040</c>, so splitting on spaces never cuts a field.
/// </para>
/// <para>
/// A device can be mounted more than once (bind mounts, a container's volumes): the lines agree on its type, and lines
/// that don't — or none — leave it unknown rather than guessed.
/// </para>
/// </remarks>
internal static class MountTable
{
    /// <summary>The field that ends the optional fields, before the type.</summary>
    private const string Separator = "-";

    /// <summary>Where the device's <c>major:minor</c> is on a line: the third field.</summary>
    private const int DeviceField = 2;

    /// <summary>The fields before the optional ones: id, parent, device, root, mount point and options.</summary>
    private const int FixedFieldCount = 6;

    /// <summary>
    /// The file system type <paramref name="mountInfo"/> gives the device <paramref name="deviceId"/> (<c>st_dev</c>,
    /// as <c>LinuxFileStatusReader</c> makes it with glibc's <c>makedev</c>); <c>null</c> when no line names the device
    /// or its lines disagree.
    /// </summary>
    public static string? FileSystemTypeOf(string mountInfo, long deviceId)
    {
        ArgumentNullException.ThrowIfNull(mountInfo);

        var device = DeviceNumberOf(deviceId);
        string? found = null;
        foreach (var line in mountInfo.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length <= FixedFieldCount || !string.Equals(fields[DeviceField], device, StringComparison.Ordinal))
                continue;

            var separator = Array.IndexOf(fields, Separator, FixedFieldCount);
            if (separator < 0 || separator + 1 >= fields.Length)
                continue;

            var type = fields[separator + 1];
            if (found is null)
                found = type;
            else if (!string.Equals(found, type, StringComparison.Ordinal))
                return null;
        }

        return found;
    }

    /// <summary>
    /// <paramref name="deviceId"/> as the mount table writes it, <c>major:minor</c>: the inverse of glibc's
    /// <c>makedev</c> (<c>gnu_dev_major</c> and <c>gnu_dev_minor</c>), on the unsigned bits of the stored number.
    /// </summary>
    private static string DeviceNumberOf(long deviceId)
    {
        var device = unchecked((ulong)deviceId);
        var major = ((device >> 8) & 0xfffUL) | ((device >> 32) & 0xfffff000UL);
        var minor = (device & 0xffUL) | ((device >> 12) & 0xffffff00UL);
        return string.Create(CultureInfo.InvariantCulture, $"{major}:{minor}");
    }
}
