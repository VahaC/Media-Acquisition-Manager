using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-40 — the Duplicates page as the owner's Remove this copy reads it, over real HTTP: the list (with each file's verdict)
/// and the request the page sends for one of its files, with the identity the page showed.
/// </summary>
public static class CopyRemovalHttpExtensions
{
    private const string DuplicatesUrl = "/api/library/duplicates";

    /// <summary>The Duplicates list as <paramref name="token"/>'s user reads it, with <paramref name="query"/> when given
    /// (<c>?class=SameWork</c>). Throws unless the answer is 200.</summary>
    public static async Task<LibraryDuplicateListResponse> DuplicatesAsync(
        this MediaAcquisitionManagerApiFactory factory, string token, string query = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        using var response = await factory.GetHttpRequest(DuplicatesUrl + query, token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The Duplicates list answered {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<LibraryDuplicateListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>The file of <paramref name="group"/> at <paramref name="appPath"/>, the app's. Throws when the group has
    /// none.</summary>
    public static LibraryDuplicateFileDto FileAt(this LibraryDuplicateGroupDto group, string appPath)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Files.SingleOrDefault(file => file.File.AppPath == appPath)
            ?? throw new InvalidOperationException($"The group has no file at {appPath}.");
    }

    /// <summary>What the page sends to remove the file of <paramref name="group"/> at <paramref name="appPath"/>: the group's
    /// key, the file's id, and the identity the page showed of it.</summary>
    public static RemoveCopyRequest RemoveRequestFor(this LibraryDuplicateGroupDto group, string appPath)
    {
        var file = group.FileAt(appPath);
        return new RemoveCopyRequest(group.Key, file.File.Id, file.DeviceId, file.Inode, file.ReadSizeBytes);
    }
}
