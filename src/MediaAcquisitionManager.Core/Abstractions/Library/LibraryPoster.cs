namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-35 — a library item's poster, as the library served it, whole.</summary>
/// <param name="Content">Its bytes.</param>
/// <param name="MediaType">Its media type, always <c>image/jpeg</c>: the app asks the library for a JPEG and keeps nothing
/// else, so it never serves a type it didn't check.</param>
public sealed record LibraryPoster(byte[] Content, string MediaType);
