using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>What an audit row's action was done to.</summary>
/// <param name="Kind">What kind of row <paramref name="Id"/> names.</param>
/// <param name="Id">The target's id.</param>
/// <param name="Name">Its display name, copied onto the row so it survives the target's deletion.</param>
public sealed record AuditTarget(AuditTargetKind Kind, Guid Id, string Name)
{
    public static AuditTarget User(UserEntity user) => new(AuditTargetKind.User, user.Id, user.UserName);

    public static AuditTarget Integration(IntegrationConnectionEntity connection) =>
        new(AuditTargetKind.Integration, connection.Id, connection.Name);
}
