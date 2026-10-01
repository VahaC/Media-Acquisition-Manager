namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// The associated data a release's payload locator is sealed with (ADR 0007): the release it belongs
/// to, so a sealed locator copied onto another release fails to open instead of fetching that one's
/// payload from the wrong place.
/// </summary>
/// <remarks>
/// MAM-10 — the first code to write releases, so the purpose MAM-7 documented on
/// <c>ReleaseEntity.PayloadLocatorEncrypted</c> becomes one helper: the search seals with it and the
/// submission (MAM-13) opens with it, and a typo in either would make every stored locator unreadable.
/// </remarks>
public static class ReleasePayloadLocatorPurpose
{
    /// <summary>
    /// <c>release/{releaseId:N}/payload-locator</c>. Changing this text makes every stored locator
    /// unreadable, which is why a test pins it.
    /// </summary>
    public static string For(Guid releaseId) => $"release/{releaseId:N}/payload-locator";
}
