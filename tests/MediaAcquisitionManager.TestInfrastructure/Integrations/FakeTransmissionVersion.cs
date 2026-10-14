namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — which Transmission a <see cref="FakeTransmissionServer"/> is: what <c>session-get</c> says of its version,
/// and, through <see cref="IsAtLeast41"/>, which of the legacy RPC's two dialects it answers in — the differences
/// MAM-29 recorded between 4.0.6 and 4.1.3 and the sources of both explain (VendorSamples README, "What the samples
/// taught").
/// </summary>
/// <param name="LongVersion">What <c>session-get</c>'s <c>version</c> answers: the version and its build, such as
/// <c>4.1.3 (838877323f)</c>.</param>
/// <param name="RpcVersion"><c>rpc-version</c>: 16 for 3.00, 17 for 4.0, 18 for 4.1.0, 19 from 4.1.1 on.</param>
/// <param name="RpcVersionMinimum"><c>rpc-version-minimum</c>.</param>
/// <param name="RpcVersionSemver"><c>rpc-version-semver</c>, which Transmission sends from 4.0.0 on; <c>null</c> for an
/// older one, which doesn't send it.</param>
public sealed record FakeTransmissionVersion(string LongVersion, int RpcVersion, int RpcVersionMinimum, string? RpcVersionSemver)
{
    /// <summary>4.1.3, as <c>session-get.json</c> recorded it: rpc 19, semver 6.0.1.</summary>
    public static FakeTransmissionVersion V4_1_3 { get; } = new("4.1.3 (838877323f)", 19, 14, "6.0.1");

    /// <summary>4.0.6, as <c>4.0.6-session-get.json</c> recorded it: rpc 17, semver 5.3.0.</summary>
    public static FakeTransmissionVersion V4_0_6 { get; } = new("4.0.6 (38c164933e)", 17, 14, "5.3.0");

    /// <summary>
    /// 3.00, for the version gate: rpc 16 and no <c>rpc-version-semver</c>, as Transmission's RPC specification lists
    /// it. Not recorded — nothing older than 4.0.6 was at hand —, so its build and its <c>rpc-version-minimum</c> are
    /// 3.00's source's, not an answer's; in every other way it answers as 4.0 does.
    /// </summary>
    public static FakeTransmissionVersion V3_00 { get; } = new("3.00 (bb6b5a062e)", 16, 1, null);

    /// <summary>
    /// Whether this is 4.1 or later, which answers the legacy dialect through its JSON-RPC translation: the 409 page
    /// says <c>session_id</c> and carries <c>X-Transmission-Rpc-Version</c>, every page carries the clickjacking
    /// headers, a whole number is written <c>1.0</c>, <c>files</c> carry <c>begin_piece</c> and <c>end_piece</c>, a
    /// failed <c>free-space</c> spells its total <c>totalSize</c>, an unknown method is "no method name", an empty file
    /// is listed, and names are kept as Linux takes them. 4.0 and older: the other way in each.
    /// </summary>
    public bool IsAtLeast41 => RpcVersion >= 18;
}
