namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the user list: a search and a page, both applied by the server.</summary>
/// <param name="Search">Part of a user name, in any letter case; blank lists everyone.</param>
/// <param name="Page">Which page, counted from 1.</param>
/// <param name="PageSize">Users per page, 1 to 100; 25 unless asked otherwise, a screenful on a
/// desktop.</param>
public sealed record UserListRequest(string? Search = null, int Page = 1, int PageSize = 25);
