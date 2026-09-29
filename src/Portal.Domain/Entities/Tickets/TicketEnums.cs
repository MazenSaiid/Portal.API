namespace Portal.Domain.Entities.Tickets;

public enum TicketPriority { Low, Medium, High, Urgent }

public enum TicketStatus { New, Open, InProgress, OnHold, Resolved, Closed }

/// <summary>Where the request came from.</summary>
public enum TicketChannel { Email, Phone, WhatsApp, Sms, LiveChat, WebForm, Other }

public enum TicketEventType
{
    Created,
    Updated,
    StatusChanged,
    PriorityChanged,
    CategoryChanged,
    Assigned,
    Unassigned,
    Escalated,
    DeEscalated,
    Comment,
}
