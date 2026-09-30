using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Permissions;
using Portal.Application.Features.Sla;
using Portal.Application.Features.Tickets;
using Portal.Domain.Entities.Tickets;
using DomainPermissions = Portal.Domain.Authorization.Permissions;

namespace Portal.Application.Features.Work;

/// <summary>Builds the personal agent dashboard (Spec 005) as one read model over existing data.</summary>
public interface IDashboardService
{
    Task<AgentDashboardDto> GetAsync(DateTime? endOfDayUtc, CancellationToken ct = default);
}

public sealed class DashboardService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    ITicketService tickets,
    ITaskService tasks) : IDashboardService
{
    private const int MyTicketsLimit = 8;
    private const int QueueLimit = 5;
    private const int ActivityLimit = 10;
    private const int TaskLimit = 8;

    public async Task<AgentDashboardDto> GetAsync(DateTime? endOfDayUtc, CancellationToken ct = default)
    {
        var myTasks = await tasks.GetMineAsync(new TaskQuery(), TaskLimit, ct);
        var reminders = await tasks.GetRemindersAsync(endOfDayUtc, ct);

        if (!await permissions.CurrentUserHasAsync(currentUser, DomainPermissions.Tickets.View, ct))
            return new AgentDashboardDto(false, new DashboardSummaryDto(0, 0, 0, 0, 0, 0), [], [], [], [], myTasks, reminders);

        var me = currentUser.UserId;
        var active = TicketWorkflow.ActiveStatuses.ToList();
        var mine = db.Tickets.AsNoTracking().Where(t => t.AssigneeId == me && active.Contains(t.Status));
        var unassigned = db.Tickets.AsNoTracking().Where(t => t.AssigneeId == null && active.Contains(t.Status));
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var summary = new DashboardSummaryDto(
            MyActive: await mine.CountAsync(ct),
            InProgress: await mine.CountAsync(t => t.Status == TicketStatus.InProgress, ct),
            Escalated: await mine.CountAsync(t => t.IsEscalated, ct),
            HighPriority: await mine.CountAsync(t => t.Priority >= TicketPriority.High, ct),
            Unassigned: await unassigned.CountAsync(ct),
            ResolvedLast7Days: await db.TicketHistory.CountAsync(h =>
                h.Type == TicketEventType.StatusChanged && h.ToValue == nameof(TicketStatus.Resolved)
                && h.CreatedById == me && h.CreatedAt >= weekAgo, ct));

        // AD2 — what needs me most comes first.
        var myTickets = await Project(mine.OrderByDescending(t => t.IsEscalated).ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.LastActivityAt).Take(MyTicketsLimit), active).ToListAsync(ct);

        // AD3 — the most urgent, longest-waiting tickets first.
        var queue = await Project(unassigned.OrderByDescending(t => t.Priority).ThenBy(t => t.CreatedAt).Take(QueueLimit), active)
            .ToListAsync(ct);

        // AD4 — what teammates did on my tickets.
        var teamActivity = await db.TicketHistory.AsNoTracking()
            .Where(h => h.Ticket.AssigneeId == me && h.CreatedById != null && h.CreatedById != me)
            .OrderByDescending(h => h.Id)
            .Take(ActivityLimit)
            .Select(h => new TeamActivityDto(h.Id, h.TicketId, h.Ticket.Subject, h.Type, h.FromValue, h.ToValue, h.Message, h.CreatedAt,
                db.Users.Where(u => u.Id == h.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()))
            .ToListAsync(ct);

        var team = await tickets.GetAssigneesAsync(ct);

        return new AgentDashboardDto(true, summary, myTickets, queue, team, teamActivity, myTasks, reminders);
    }

    private IQueryable<DashboardTicketDto> Project(IQueryable<Ticket> source, List<TicketStatus> active) =>
        source.Select(t => new DashboardTicketDto(
            t.Id, t.Subject, t.Priority, t.Status, t.IsEscalated, t.Category.Name, t.CreatedAt, t.LastActivityAt,
            new DashboardCustomerDto(t.CustomerId, t.Customer.Name, t.Customer.Email, t.Customer.Phone,
                db.Tickets.Count(o => o.CustomerId == t.CustomerId && o.Id != t.Id && active.Contains(o.Status))),
            new TicketSlaDto(t.CreatedAt, t.FirstResponseDueAt, t.FirstRespondedAt, t.ResolutionDueAt, t.ResolvedAt ?? t.ClosedAt)));
}
