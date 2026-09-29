using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Features.Permissions;
using Portal.Domain.Entities.Tickets;
using static Portal.Application.Features.Tickets.TicketRules;

namespace Portal.Application.Features.Tickets;

/// <summary>Moves tickets through their life: status, assignment, escalation and comments. Every action is recorded (K5).</summary>
public interface ITicketWorkflowService
{
    Task<TicketDto> ChangeStatusAsync(int id, ChangeStatusRequest request, CancellationToken ct = default);
    Task<TicketDto> AssignAsync(int id, AssignTicketRequest request, CancellationToken ct = default);
    Task<TicketDto> EscalateAsync(int id, EscalateTicketRequest request, CancellationToken ct = default);
    Task<TicketDto> DeEscalateAsync(int id, DeEscalateTicketRequest request, CancellationToken ct = default);
    Task<TicketHistoryDto> AddCommentAsync(int id, TicketCommentRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(int id, CancellationToken ct = default);
}

public sealed class TicketWorkflowService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    ITicketService tickets,
    IValidator<ChangeStatusRequest> statusValidator,
    IValidator<EscalateTicketRequest> escalateValidator,
    IValidator<DeEscalateTicketRequest> deEscalateValidator,
    IValidator<TicketCommentRequest> commentValidator) : ITicketWorkflowService
{
    public async Task<TicketDto> ChangeStatusAsync(int id, ChangeStatusRequest request, CancellationToken ct = default)
    {
        await statusValidator.ValidateAndThrowAsync(request, ct);
        var ticket = await LoadAsync(id, ct);
        var from = ticket.Status;
        var to = request.Status;

        if (from == to)
            throw new BusinessRuleException($"The ticket is already {Label(to)}.");
        if (!TicketWorkflow.CanMove(from, to)) // W1
            throw new BusinessRuleException($"A ticket can't move from {Label(from)} to {Label(to)}.");
        if (to == TicketStatus.InProgress && ticket.AssigneeId is null) // W2
            throw new BusinessRuleException("Assign the ticket to an agent before starting work on it.");

        var now = DateTime.UtcNow;
        switch (to) // W6
        {
            case TicketStatus.Resolved: ticket.ResolvedAt = now; break;
            case TicketStatus.Closed: ticket.ClosedAt = now; break;
            case TicketStatus.Open when from is TicketStatus.Resolved or TicketStatus.Closed:
                ticket.ResolvedAt = null;
                ticket.ClosedAt = null;
                break;
        }

        ticket.Status = to;
        ticket.History.Add(History(TicketEventType.StatusChanged, from.ToString(), to.ToString(), Trim(request.Comment)));
        return await SaveAsync(ticket, ct);
    }

    public async Task<TicketDto> AssignAsync(int id, AssignTicketRequest request, CancellationToken ct = default)
    {
        var ticket = await LoadAsync(id, ct);
        EnsureNotClosed(ticket);
        var newName = await AuthorizeAssignmentAsync(db, permissions, currentUser, ticket.AssigneeId, request.AssigneeId, ct);
        if (ticket.AssigneeId == request.AssigneeId) return await tickets.GetByIdAsync(id, ct);

        var oldName = ticket.Assignee is null ? null : ticket.Assignee.FullName;
        ticket.AssigneeId = request.AssigneeId;
        ticket.History.Add(request.AssigneeId is null
            ? History(TicketEventType.Unassigned, from: oldName)
            : History(TicketEventType.Assigned, oldName, newName));

        if (request.AssigneeId is not null && ticket.Status == TicketStatus.New) // W4
        {
            ticket.Status = TicketStatus.Open;
            ticket.History.Add(History(TicketEventType.StatusChanged, nameof(TicketStatus.New), nameof(TicketStatus.Open), "Opened on assignment."));
        }
        return await SaveAsync(ticket, ct);
    }

    public async Task<TicketDto> EscalateAsync(int id, EscalateTicketRequest request, CancellationToken ct = default)
    {
        await escalateValidator.ValidateAndThrowAsync(request, ct);
        var ticket = await LoadAsync(id, ct);
        if (!TicketWorkflow.IsActive(ticket.Status)) // E2
            throw new BusinessRuleException($"{Label(ticket.Status)} tickets can't be escalated.");
        if (ticket.IsEscalated)
            throw new BusinessRuleException("The ticket is already escalated.");

        ticket.IsEscalated = true;
        ticket.EscalatedAt = DateTime.UtcNow;
        ticket.EscalationReason = request.Reason.Trim();
        ticket.History.Add(History(TicketEventType.Escalated, message: ticket.EscalationReason));

        if (ticket.Priority < TicketPriority.High) // E1
        {
            ticket.History.Add(History(TicketEventType.PriorityChanged, ticket.Priority.ToString(), nameof(TicketPriority.High), "Raised by escalation."));
            ticket.Priority = TicketPriority.High;
        }
        return await SaveAsync(ticket, ct);
    }

    public async Task<TicketDto> DeEscalateAsync(int id, DeEscalateTicketRequest request, CancellationToken ct = default)
    {
        await deEscalateValidator.ValidateAndThrowAsync(request, ct);
        var ticket = await LoadAsync(id, ct);
        EnsureNotClosed(ticket);
        if (!ticket.IsEscalated)
            throw new BusinessRuleException("The ticket is not escalated.");

        ticket.IsEscalated = false; // E3 — priority is kept
        ticket.EscalatedAt = null;
        ticket.EscalationReason = null;
        ticket.History.Add(History(TicketEventType.DeEscalated, message: Trim(request.Comment)));
        return await SaveAsync(ticket, ct);
    }

    public async Task<TicketHistoryDto> AddCommentAsync(int id, TicketCommentRequest request, CancellationToken ct = default)
    {
        await commentValidator.ValidateAndThrowAsync(request, ct);
        var ticket = await LoadAsync(id, ct);
        EnsureNotClosed(ticket);

        var entry = History(TicketEventType.Comment, message: request.Content.Trim());
        ticket.History.Add(entry);
        ticket.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return (await GetHistoryAsync(id, ct)).Single(h => h.Id == entry.Id);
    }

    public async Task<IReadOnlyList<TicketHistoryDto>> GetHistoryAsync(int id, CancellationToken ct = default)
    {
        if (!await db.Tickets.AnyAsync(t => t.Id == id, ct)) throw new NotFoundException("Ticket", id);

        return await db.TicketHistory.AsNoTracking()
            .Where(h => h.TicketId == id)
            .OrderBy(h => h.Id)
            .Select(h => new TicketHistoryDto(h.Id, h.Type, h.FromValue, h.ToValue, h.Message, h.CreatedAt,
                db.Users.Where(u => u.Id == h.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()))
            .ToListAsync(ct);
    }

    private async Task<Ticket> LoadAsync(int id, CancellationToken ct) =>
        await db.Tickets.Include(t => t.Assignee).FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("Ticket", id);

    private async Task<TicketDto> SaveAsync(Ticket ticket, CancellationToken ct)
    {
        ticket.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await tickets.GetByIdAsync(ticket.Id, ct);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Label(TicketStatus status) => status switch
    {
        TicketStatus.InProgress => "In progress",
        TicketStatus.OnHold => "On hold",
        _ => status.ToString(),
    };
}
