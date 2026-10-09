namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-24 — the installation owner's master switch for replacing copies (ADR 0033): whether the Download dialog may
/// offer Replace after verification at all. Saved by the Media folders page's one Save, beside the allowed roots and
/// the per-folder ticks (<see cref="ReplacementFolderEntity"/>).
/// </summary>
/// <remarks>
/// <para>
/// One row at most, keyed <see cref="SingletonId"/> like <see cref="InstallationEntity"/>, and none until the owner
/// first switches replacement on: no row means off, which is the state of a fresh installation and of one upgraded to
/// this version, so an upgrade can never open a gate nobody opened (ADR 0033).
/// </para>
/// <para>
/// A table of its own rather than a column on <see cref="InstallationEntity"/>, whose row says who owns the
/// installation and nothing else. No <c>CreatedUtc</c>: the audit rows say when the switch changed and who changed it.
/// </para>
/// </remarks>
public class ReplacementSettingsEntity : BaseEntity
{
    /// <summary>The key of the one row.</summary>
    public static readonly Guid SingletonId = new("c0000000-0000-0000-0000-000000000002");

    public ReplacementSettingsEntity() => Id = SingletonId;

    /// <summary>Whether the owner has switched replacement on; off by default.</summary>
    public bool IsEnabled { get; set; }
}
