namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>One library of a media server and the folders it covers.</summary>
/// <param name="SourceLocationId">The server's own id of the library.</param>
/// <param name="Name">The library's name ("Фільми").</param>
/// <param name="ServicePaths">Its root folders as the server sees them.</param>
public sealed record LibraryLocation(string SourceLocationId, string Name, IReadOnlyList<string> ServicePaths);
