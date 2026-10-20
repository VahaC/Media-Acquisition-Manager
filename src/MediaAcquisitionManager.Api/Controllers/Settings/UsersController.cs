using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Http;
using MediaAcquisitionManager.Api.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Controllers.Settings;

/// <summary>The installation owner's user management, behind the Users settings page.</summary>
/// <remarks>
/// MAM-4 — owner-only as a whole, the reads included: a list of who uses the installation, and when
/// they last signed in, is personal data, not a setting only a change of which needs guarding. The
/// writes would be owner-only here without the attribute, by <see cref="SettingsWritesAreOwnerOnlyConvention"/>.
/// </remarks>
[ApiController]
[Authorize]
[RequireInstallationOwner]
[Route("api/settings/users")]
public class UsersController(IUserAccountService accounts) : ControllerBase
{
    private AccountCaller Caller => new(User.GetUserId(), User.GetSessionId());

    [HttpGet]
    public async Task<ActionResult<UserListResponse>> List([FromQuery] UserListRequest request, CancellationToken cancellationToken)
    {
        var result = await accounts.ListAsync(request.Search, request.Page, request.PageSize, cancellationToken);
        return result.Status switch
        {
            UserListStatus.Listed => Ok(result.List),
            _ => this.BadRequestError(result.Error!),
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken cancellationToken) =>
        await accounts.GetAsync(id, cancellationToken) is { } user ? Ok(user) : NotFound();

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        // A missing field is a blank one: the rules answer both with the same sentence.
        var result = await accounts.CreateAsync(request.UserName ?? "", request.Password ?? "", Caller, cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => CreatedAtAction(nameof(Get), new { id = result.User!.Id }, result.User),
            UserAccountStatus.Invalid => this.BadRequestError(result.Error!),
            _ => this.ConflictError(result.Error!),
        };
    }

    /// <summary>Ends every session the user holds at once; their access token stops working with its
    /// next request, not when it expires. MAM-5 — and revokes their personal access tokens.</summary>
    [HttpPost("{id:guid}/disable")]
    public async Task<ActionResult<UserDto>> Disable(Guid id, CancellationToken cancellationToken)
    {
        var result = await accounts.DisableAsync(id, Caller, cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => Ok(result.User),
            UserAccountStatus.NotFound => NotFound(),
            _ => this.ConflictError(result.Error!),
        };
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<ActionResult<UserDto>> Enable(Guid id, CancellationToken cancellationToken)
    {
        var result = await accounts.EnableAsync(id, Caller, cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => Ok(result.User),
            _ => NotFound(),
        };
    }

    /// <summary>Ends every session of the user except the caller's own, so an owner resetting their
    /// own password stays signed in here. MAM-5 — and revokes every personal access token of the user,
    /// the owner's own included.</summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        // A missing password is a blank one: the rule answers both with the same sentence.
        var result = await accounts.ResetPasswordAsync(id, request.Password ?? "", Caller, cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => NoContent(),
            UserAccountStatus.NotFound => NotFound(),
            _ => this.BadRequestError(result.Error!),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await accounts.DeleteAsync(id, Caller, cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => NoContent(),
            UserAccountStatus.NotFound => NotFound(),
            _ => this.ConflictError(result.Error!),
        };
    }

    /// <summary>
    /// MAM-5 — revokes every personal access token of the user: 204, also when they hold none; a bare 404 for a user
    /// that doesn't exist. Their scripts stop with their next request (ADR 0040). Needs no session id, so it reads the
    /// caller by their user id, not <see cref="Caller"/>; a token never gets here (the Settings convention).
    /// </summary>
    [HttpDelete("{id:guid}/tokens")]
    public async Task<IActionResult> RevokeTokens(Guid id, CancellationToken cancellationToken)
    {
        var result = await accounts.RevokeTokensAsync(id, User.GetUserId(), cancellationToken);
        return result.Status switch
        {
            UserAccountStatus.Succeeded => NoContent(),
            _ => NotFound(),
        };
    }
}
