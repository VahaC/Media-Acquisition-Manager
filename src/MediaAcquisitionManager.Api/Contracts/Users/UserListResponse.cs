namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One page of the user list.</summary>
/// <param name="Items">The users on this page, ordered by user name, letter case ignored.</param>
/// <param name="TotalCount">Every user matching the search, across all pages.</param>
/// <param name="Page">The page, counted from 1, as asked for.</param>
/// <param name="PageSize">The page size, as asked for.</param>
public sealed record UserListResponse(IReadOnlyList<UserDto> Items, int TotalCount, int Page, int PageSize);
