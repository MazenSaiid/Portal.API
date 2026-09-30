using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Sla;

/// <summary>SLA status of one ticket, computed on read from the stored dates (Spec 007, S5).</summary>
public sealed record TicketSlaDto(
    DateTime StartedAt,
    DateTime? FirstResponseDueAt,
    DateTime? FirstRespondedAt,
    DateTime? ResolutionDueAt,
    DateTime? ResolvedAt)
{
    public SlaState FirstResponseState => SlaCalculator.State(StartedAt, FirstResponseDueAt, FirstRespondedAt, DateTime.UtcNow);
    public SlaState ResolutionState => SlaCalculator.State(StartedAt, ResolutionDueAt, ResolvedAt, DateTime.UtcNow);
}

public sealed record SlaPolicyDto(TicketPriority Priority, int FirstResponseMinutes, int ResolutionMinutes);

public sealed record AutomationSettingsDto(bool AutoAssignEnabled);

public sealed record EscalationRuleDto(
    int Id,
    string Name,
    bool IsActive,
    SlaTrigger Trigger,
    int? ThresholdMinutes,
    TicketPriority? MinPriority,
    bool Escalate,
    TicketPriority? RaisePriorityTo,
    bool NotifyAssignee,
    bool NotifySupervisors,
    int TimesFired);

public sealed record EscalationRuleRequest(
    string Name,
    bool IsActive,
    SlaTrigger Trigger,
    int? ThresholdMinutes,
    TicketPriority? MinPriority,
    bool Escalate,
    TicketPriority? RaisePriorityTo,
    bool NotifyAssignee,
    bool NotifySupervisors);

public sealed record NotificationDto(
    long Id,
    NotificationType Type,
    string Title,
    string Message,
    int? TicketId,
    bool IsRead,
    DateTime CreatedAt);
