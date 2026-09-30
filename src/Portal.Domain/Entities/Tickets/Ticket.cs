using Portal.Domain.Common;
using Portal.Domain.Entities.Customers;

namespace Portal.Domain.Entities.Tickets;

public class Ticket : AuditableEntity
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public TicketCategory Category { get; set; } = null!;

    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketChannel Channel { get; set; } = TicketChannel.Email;

    public Guid? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }

    public bool IsEscalated { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public string? EscalationReason { get; set; }

    /// <summary>Last time anything happened on the ticket, including comments. Drives "recently active" sorting.</summary>
    public DateTime LastActivityAt { get; set; }

    // ---------- SLA (Spec 007) ----------
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? FirstRespondedAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }

    /// <summary>The moment 75 % of the resolution window is used; stored so "at risk" is a simple comparison.</summary>
    public DateTime? ResolutionAtRiskAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<TicketHistoryEntry> History { get; set; } = new List<TicketHistoryEntry>();

    /// <summary>Human-friendly code, e.g. TCK-00042.</summary>
    public static string FormatCode(int id) => $"TCK-{id:D5}";
}

public class TicketCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One line of a ticket's history: a comment or a recorded change. Written only by the server, never edited (K5).
/// Values are text snapshots so the history stays readable after renames or deletions.
/// </summary>
public class TicketHistoryEntry : AuditableEntity
{
    /// <summary>Sequential, so entries written in the same save keep their order.</summary>
    public long Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public TicketEventType Type { get; set; }
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public string? Message { get; set; }
}
