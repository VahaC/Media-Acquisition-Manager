using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>
/// MAM-30 — a library folder a download client whose destinations are folders has no folder for (ADR 0039): its
/// path mappings lead nowhere back to it, or to it from two places, the folder they lead to is too long to store, or
/// the library's own integration can't place it in the app. Listed beside the client's folders with how to fix it,
/// never hidden.
/// </summary>
/// <param name="LibraryName">The library's name ("Фільми").</param>
/// <param name="Folder">The library's folder as the app knows it: inside the app, or — when its own integration's
/// mappings give it no place there — as that integration reported it.</param>
/// <param name="Problem">The sentence naming the integration, the library and the fix, as a message the Api renders
/// (ADR 0042).</param>
public sealed record UnplacedLibraryFolder(string LibraryName, string Folder, Message Problem);
