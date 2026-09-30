using Portal.Domain.Common;
using Portal.Domain.Entities.Tickets;

namespace Portal.Domain.Entities.Sla;

/// <summary>Response and resolution targets for one priority (Spec 007, S1–S2).</summary>
public class SlaPolicy
{
    public int Id { get; set; }
    public TicketPriority Priority { get; set; }
    public int FirstResponseMinutes { get; set; }
    public int ResolutionMinutes { get; set; }
}

/// <summary>Single-row settings table (Id = 1).</summary>
public class AutomationSettings
{
    public int Id { get; set; } = 1;
    public bool AutoAssignEnabled { get; set; }
}

public enum SlaTrigger { FirstResponseBreached, ResolutionAtRisk, ResolutionBreached, UnassignedFor }

/// <summary>An admin-defined automation: when <see cref="Trigger"/> matches an active ticket, run the actions once (S7).</summary>
public class EscalationRule : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public SlaTrigger Trigger { get; set; }

    /// <summary>Minutes for <see cref="SlaTrigger.UnassignedFor"/>.</summary>
    public int? ThresholdMinutes { get; set; }

    /// <summary>Only tickets at or above this priority; null = all.</summary>
    public TicketPriority? MinPriority { get; set; }

    public bool Escalate { get; set; }
    public TicketPriority? RaisePriorityTo { get; set; }
    public bool NotifyAssignee { get; set; }
    public bool NotifySupervisors { get; set; }
}

/// <summary>Remembers that a rule already fired for a ticket, so it never fires twice.</summary>
public class EscalationRuleExecution
{
    public int RuleId { get; set; }
    public EscalationRule Rule { get; set; } = null!;
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public DateTime ExecutedAt { get; set; }
}

public enum NotificationType { TicketAssigned, TicketEscalated, SlaAlert }

/// <summary>An in-app message for one user (S9).</summary>
public class Notification
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>Ticket the notification is about; no FK so notifications survive ticket deletion.</summary>
    public int? TicketId { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
