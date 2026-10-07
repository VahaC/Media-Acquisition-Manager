namespace MediaAcquisitionManager.Api.Services.Posters;

/// <summary>
/// MAM-33, MAM-35 — how the app keeps a poster it fetched, one rule for the recommendations' (<c>&lt;data&gt;/posters/</c>)
/// and the Library's (<c>&lt;data&gt;/library-posters/</c>): whole or not at all, so a request reading the cache never gets
/// half a poster.
/// </summary>
public static class PosterFiles
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> in <paramref name="directory"/> whole or not at all: to a
    /// file of its own first, then moved into place. Best effort — a disk that refuses costs only the next request a fetch,
    /// so the poster is served whatever happens here; <paramref name="logger"/>, the caller's, says what went wrong.
    /// </summary>
    public static async Task KeepAsync(string directory, string path, byte[] content, ILogger logger, CancellationToken cancellationToken)
    {
        // A dot first, so neither posters route — each takes no name starting with one — can ever serve it half written.
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another request fetched the same poster and kept it first: one image, so either copy will do.
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Warning, so it ships off the box: a data volume that refuses writes is the operator's to fix.
            logger.LogWarning(exception, "The poster {FileName} was fetched but couldn't be kept in {Directory}.", Path.GetFileName(path), directory);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover starts with a dot, so nothing ever serves it; failing the poster over it would be worse.
                logger.LogWarning(exception, "The temporary poster file {File} couldn't be removed.", Path.GetFileName(temporary));
            }
        }
    }
}
