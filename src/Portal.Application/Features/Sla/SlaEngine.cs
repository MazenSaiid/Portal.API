using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Sla;

/// <summary>
/// Evaluates escalation rules against active tickets (Spec 007, S7–S8). Called every minute by the SLA monitor,
/// and directly by tests with an explicit <c>now</c>.
/// </summary>
public interface ISlaEngine
{
    /// <returns>How many rule executions ran.</returns>
    Task<int> RunAsync(DateTime now, CancellationToken ct = default);
}

public sealed class SlaEngine(IApplicationDbContext db, INotifier notifier, ILogger<SlaEngine> logger) : ISlaEngine
{
    public async Task<int> RunAsync(DateTime now, CancellationToken ct = default)
    {
        var rules = await db.EscalationRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        if (rules.Count == 0) return 0;

        var active = TicketWorkflow.ActiveStatuses.ToList();
        var tickets = await db.Tickets.Include(t => t.Assignee).Where(t => active.Contains(t.Status)).ToListAsync(ct);
        var done = (await db.EscalationRuleExecutions.AsNoTracking().Select(e => new { e.RuleId, e.TicketId }).ToListAsync(ct))
            .Select(e => (e.RuleId, e.TicketId)).ToHashSet();

        var fired = 0;
        foreach (var rule in rules)
        foreach (var ticket in tickets)
        {
            if (done.Contains((rule.Id, ticket.Id)) || !Matches(rule, ticket, now)) continue;

            await ExecuteAsync(rule, ticket, now, ct);
            db.EscalationRuleExecutions.Add(new EscalationRuleExecution { RuleId = rule.Id, TicketId = ticket.Id, ExecutedAt = now });
            done.Add((rule.Id, ticket.Id));
            fired++;
        }

        if (fired > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SLA engine ran {Count} rule execution(s)", fired);
        }
        return fired;
    }

    private static bool Matches(EscalationRule rule, Ticket t, DateTime now)
    {
        if (rule.MinPriority is { } min && t.Priority < min) return false;
        return rule.Trigger switch
        {
            SlaTrigger.FirstResponseBreached => t.FirstRespondedAt is null && t.FirstResponseDueAt < now,
            SlaTrigger.ResolutionBreached => t.ResolutionDueAt < now,
            SlaTrigger.ResolutionAtRisk => t.ResolutionAtRiskAt <= now && t.ResolutionDueAt >= now,
            SlaTrigger.UnassignedFor => t.AssigneeId is null && rule.ThresholdMinutes is { } m && t.CreatedAt.AddMinutes(m) <= now,
            _ => false,
        };
    }

    private async Task ExecuteAsync(EscalationRule rule, Ticket t, DateTime now, CancellationToken ct)
    {
        var code = Ticket.FormatCode(t.Id);
        var why = Describe(rule);

        if (rule.RaisePriorityTo is { } target && target > t.Priority) // SL3 — never lowers
        {
            t.History.Add(new TicketHistoryEntry { Type = TicketEventType.PriorityChanged, FromValue = t.Priority.ToString(), ToValue = target.ToString(), Message = $"Rule \"{rule.Name}\": {why}." });
            t.Priority = target;
            await db.ApplyPolicyAsync(t, t.CreatedAt, ct); // S4
        }

        if (rule.Escalate && !t.IsEscalated)
        {
            t.IsEscalated = true;
            t.EscalatedAt = now;
            t.EscalationReason = $"Automatic — {rule.Name}: {why}.";
            t.History.Add(new TicketHistoryEntry { Type = TicketEventType.Escalated, Message = t.EscalationReason });
            if (t.Priority < TicketPriority.High) // Spec 004, E1 applies to automatic escalation too
            {
                t.History.Add(new TicketHistoryEntry { Type = TicketEventType.PriorityChanged, FromValue = t.Priority.ToString(), ToValue = nameof(TicketPriority.High), Message = "Raised by escalation." });
                t.Priority = TicketPriority.High;
                await db.ApplyPolicyAsync(t, t.CreatedAt, ct);
            }
        }

        var title = $"{code}: {why}";
        var message = $"\"{t.Subject}\" — rule \"{rule.Name}\".";
        if (rule.NotifyAssignee && t.AssigneeId is { } assignee)
            notifier.Notify(assignee, NotificationType.SlaAlert, title, message, t.Id);
        if (rule.NotifySupervisors)
            await notifier.NotifySupervisorsAsync(NotificationType.SlaAlert, title, message, t.Id,
                except: rule.NotifyAssignee ? t.AssigneeId : null, ct);

        t.LastActivityAt = now;
    }

    private static string Describe(EscalationRule rule) => rule.Trigger switch
    {
        SlaTrigger.FirstResponseBreached => "first response target missed",
        SlaTrigger.ResolutionBreached => "resolution target missed",
        SlaTrigger.ResolutionAtRisk => "resolution target at risk",
        SlaTrigger.UnassignedFor => $"unassigned for over {rule.ThresholdMinutes} minutes",
        _ => "rule matched",
    };
}
