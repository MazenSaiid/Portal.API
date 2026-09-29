using Portal.Domain.Common;

namespace Portal.Domain.Entities.Customers;

/// <summary>A person to talk to at a (usually company) customer.</summary>
public class CustomerContact : AuditableEntity
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Something that happened with the customer. Immutable once logged.</summary>
public class CustomerInteraction : AuditableEntity
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public InteractionType Type { get; set; }
    public InteractionDirection Direction { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTime OccurredAt { get; set; }
}

public class CustomerNote : AuditableEntity
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string Content { get; set; } = string.Empty;
}

public class CustomerAttachment : AuditableEntity
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>Original file name, for display and download only — never used as a path.</summary>
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Random key the file is stored under.</summary>
    public string StorageKey { get; set; } = string.Empty;
}
