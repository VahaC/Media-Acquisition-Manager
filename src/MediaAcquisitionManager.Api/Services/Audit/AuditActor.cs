using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>Who did what an audit row records.</summary>
/// <param name="UserId">The acting user's id.</param>
/// <param name="UserName">Their user name, copied onto the row so it survives their deletion.</param>
public sealed record AuditActor(Guid UserId, string UserName)
{
    public static AuditActor From(UserEntity user) => new(user.Id, user.UserName);
}
