namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-30 — a media library folder a folder client has no folder for (ADR 0039): listed under the Add or Edit
/// destination dialog's folders, never hidden, so the owner learns which library can't be picked and how to fix it.
/// </summary>
/// <param name="LibraryName">The library's name ("Фільми").</param>
/// <param name="Folder">The folder as far as the app knows it: inside the app, or as the media server reports it when
/// its mappings bring it into no folder of the app.</param>
/// <param name="Problem">One readable sentence saying why the client has no folder for it and how to fix that.</param>
public sealed record UnplacedLibraryFolderDto(string LibraryName, string Folder, string Problem);
