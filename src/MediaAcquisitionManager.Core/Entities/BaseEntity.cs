namespace MediaAcquisitionManager.Core.Entities;

/// <summary>The root of every persisted entity: a Guid key the app assigns itself.</summary>
/// <remarks>
/// Assigned on construction rather than by the database, so a new row's id is known before
/// <c>SaveChanges</c> — a foreign key or an audit row written in the same save can point at it.
/// </remarks>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
