using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Interfaces;
using Portal.Application.Features.Tickets;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Sla;

/// <summary>SLA helpers used by the ticket services and the rule engine.</summary>
public static class TicketSla
{
    /// <summary>S1/S4 — (re)computes due dates from the policy of the ticket's current priority.</summary>
    public static async Task ApplyPolicyAsync(this IApplicationDbContext db, Ticket ticket, DateTime createdAt, CancellationToken ct)
    {
        var policy = await db.SlaPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Priority == ticket.Priority, ct);
        if (policy is null) return; // no targets configured for this priority
        var dates = SlaCalculator.For(createdAt, policy);
        ticket.FirstResponseDueAt = dates.FirstResponseDueAt;
        ticket.ResolutionDueAt = dates.ResolutionDueAt;
        ticket.ResolutionAtRiskAt = dates.ResolutionAtRiskAt;
    }

    /// <summary>S3 — the first comment or status change counts as the first response.</summary>
    public static void RecordFirstResponse(this Ticket ticket, DateTime now) => ticket.FirstRespondedAt ??= now;

    public static async Task<bool> IsAutoAssignEnabledAsync(this IApplicationDbContext db, CancellationToken ct) =>
        await db.AutomationSettings.AsNoTracking().Select(s => s.AutoAssignEnabled).FirstOrDefaultAsync(ct);

    /// <summary>
    /// S6 — the active agent with the fewest active tickets; ties go to whoever received a ticket least recently.
    /// </summary>
    public static async Task<(Guid Id, string Name)?> PickLeastLoadedAgentAsync(this IApplicationDbContext db, CancellationToken ct)
    {
        var active = TicketWorkflow.ActiveStatuses.ToList();
        var pick = await db.Agents()
            .Select(u => new
            {
                u.Id,
                Name = u.FirstName + " " + u.LastName,
                Load = db.Tickets.Count(t => t.AssigneeId == u.Id && active.Contains(t.Status)),
                LastAssigned = db.Tickets.Where(t => t.AssigneeId == u.Id).Max(t => (DateTime?)t.CreatedAt),
            })
            .OrderBy(a => a.Load).ThenBy(a => a.LastAssigned.HasValue).ThenBy(a => a.LastAssigned)
            .FirstOrDefaultAsync(ct);
        return pick is null ? null : (pick.Id, pick.Name);
    }
}
