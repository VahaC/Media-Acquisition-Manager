namespace MediaAcquisitionManager.Infrastructure.Security;

/// <summary>A secret the app generates on its first start when configuration leaves it blank.</summary>
/// <param name="ConfigurationKey">The configuration key it fills, e.g. <c>Jwt:Secret</c>.</param>
/// <param name="FileName">Its file inside the secrets directory. Part of the on-disk contract of an
/// installation: renaming it would make an upgraded app generate a new value next to the old one.</param>
/// <param name="ByteLength">How many random bytes a generated value has (stored base64-encoded).</param>
/// <param name="LossConsequence">What replacing the value does, finishing the sentence "a new value
/// …" — quoted when the file is unusable, so the operator knows what regenerating would cost.</param>
internal sealed record FirstRunSecret(string ConfigurationKey, string FileName, int ByteLength, string LossConsequence);
