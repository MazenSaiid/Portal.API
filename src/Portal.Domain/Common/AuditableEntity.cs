namespace Portal.Domain.Common;

/// <summary>Entities whose creation and last change are stamped automatically by the DbContext.</summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
}
