using Portal.Domain.Common;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Tickets;

namespace Portal.Domain.Entities.Work;

/// <summary>A private to-do / reminder of one user, optionally about a ticket or customer (Spec 005, D2).</summary>
public class AgentTask : AuditableEntity
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime? DueAt { get; set; }
    public bool IsDone { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}

/// <summary>A canned answer. <see cref="OwnerId"/> null means shared with the whole team (D4).</summary>
public class QuickReply : AuditableEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public Guid? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }
}
