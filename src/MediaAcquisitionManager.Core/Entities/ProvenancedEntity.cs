using System.ComponentModel.DataAnnotations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// A fact the core stores together with the source that asserted it (ADR 0006): a work's title or
/// identifier, a version, a file name, a release. Not an EF entity itself, like
/// <see cref="AuditableEntity"/>; each subclass is a table carrying these columns.
/// </summary>
/// <remarks>
/// <para>
/// MAM-7 — plain columns on a base class rather than a complex or owned type: plain columns can be
/// indexed and carry a foreign key (a complex type can't), and they have none of an owned type's
/// quirks when the owner is null.
/// </para>
/// <para>
/// <see cref="AuditableEntity.CreatedUtc"/> is when the fact was first seen; <see cref="ObservedUtc"/>
/// when its source last reported it.
/// </para>
/// </remarks>
public abstract class ProvenancedEntity : AuditableEntity
{
    /// <summary>What kind of source asserted the fact. Survives the removal of the connection.</summary>
    public ProvenanceSourceKind SourceKind { get; set; }

    /// <summary>
    /// The integration that reported the fact; <c>null</c> for the app's own file inspection, or once
    /// that integration is removed (the foreign key sets it null: a fact outlives its reporter).
    /// </summary>
    public Guid? SourceConnectionId { get; set; }

    /// <summary>
    /// The integration's name when it reported the fact, copied (the audit-row pattern), so "who said
    /// so" still reads after the integration is renamed or removed.
    /// </summary>
    [MaxLength(IntegrationConnectionRules.NameMaximumLength)]
    public string? SourceName { get; set; }

    /// <summary>
    /// The source's own id of the record that carried the value: a library item id, a Torznab
    /// <c>&lt;guid&gt;</c> with any session removed (ADR 0004). 512 because such a guid can be a URL.
    /// </summary>
    /// <remarks>
    /// This is where "a work is not a library item" lives: the id is provenance, never the identity
    /// of the row that holds it. Two items of one library can assert facts about one work.
    /// </remarks>
    [MaxLength(512)]
    public string? SourceItemId { get; set; }

    /// <summary>When the source last reported the fact, from the injected <see cref="TimeProvider"/>.</summary>
    public DateTime ObservedUtc { get; set; }
}
