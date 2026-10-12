using System.Runtime.InteropServices;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// <c>struct statx</c> from <c>linux/stat.h</c>: 256 bytes, the same on every architecture. Only the fields
/// read are declared; the size keeps room for the rest, which the kernel fills.
/// </summary>
/// <remarks>
/// MAM-26 — shared by the reader and the quarantine (<see cref="LinuxCalls"/>), and with <c>stx_mnt_id</c>, which
/// the kernel fills only when asked for <see cref="LinuxCalls.MountIdReported"/> and able to (Linux 5.8 or later).
/// MAM-39 — and with <c>stx_uid</c> and <c>stx_gid</c>, which the Media folders page names when the app's user may not
/// write to a folder.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 256)]
internal struct StatxBuffer
{
    [FieldOffset(0)] public uint Mask;
    [FieldOffset(16)] public uint LinkCount;
    [FieldOffset(20)] public uint UserId;
    [FieldOffset(24)] public uint GroupId;
    [FieldOffset(28)] public ushort Mode;
    [FieldOffset(32)] public ulong Inode;
    [FieldOffset(40)] public ulong Size;
    [FieldOffset(112)] public long ModifiedSeconds;
    [FieldOffset(120)] public uint ModifiedNanoseconds;
    [FieldOffset(136)] public uint DeviceMajor;
    [FieldOffset(140)] public uint DeviceMinor;
    [FieldOffset(144)] public ulong MountId;
}
