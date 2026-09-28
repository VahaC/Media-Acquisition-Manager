namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// The one row that says this installation is set up, and who owns it. It exists once the first-run
/// setup has created the owner, and never before.
/// </summary>
/// <remarks>
/// Its key is always <see cref="SingletonId"/>. That fixed key is what makes two setups racing each
/// other safe: both may pass the "not set up yet" check, but only one insert of this row can
/// succeed, and the loser's whole save — its would-be owner included — rolls back.
/// </remarks>
public class InstallationEntity : BaseEntity
{
    /// <summary>The key of the installation row.</summary>
    public static readonly Guid SingletonId = new("c0000000-0000-0000-0000-000000000001");

    public InstallationEntity() => Id = SingletonId;

    /// <summary>The installation owner, who manages users and global settings.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>When the first-run setup created the owner.</summary>
    public DateTime SetupCompletedUtc { get; set; }
}
