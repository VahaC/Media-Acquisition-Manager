namespace MediaAcquisitionManager.Core.Entities;

/// <summary>An entity that remembers when it was created.</summary>
public abstract class AuditableEntity : BaseEntity
{
    /// <summary>When the row was created, from the injected <see cref="TimeProvider"/>.</summary>
    public DateTime CreatedUtc { get; set; }
}
