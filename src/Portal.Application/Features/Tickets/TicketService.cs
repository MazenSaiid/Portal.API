using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Models;
using Portal.Application.Features.Permissions;
using Portal.Application.Features.Sla;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;
using static Portal.Application.Features.Tickets.TicketRules;

namespace Portal.Application.Features.Tickets;

public interface ITicketService
{
    Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketListQuery query, CancellationToken ct = default);
    Task<TicketDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TicketDto> CreateAsync(CreateTicketRequest request, CancellationToken ct = default);
    Task<TicketDto> UpdateAsync(int id, UpdateTicketRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AssigneeDto>> GetAssigneesAsync(CancellationToken ct = default);
}

public sealed partial class TicketService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    INotifier notifier,
    IValidator<CreateTicketRequest> createValidator,
    IValidator<UpdateTicketRequest> updateValidator) : ITicketService
{
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketListQuery query, CancellationToken ct = default)
    {
        var tickets = db.Tickets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var codeId = ParseCode(term);
            tickets = tickets.Where(t => t.Subject.Contains(term) || t.Customer.Name.Contains(term) || (codeId != null && t.Id == codeId));
        }
        if (query.Status is { Count: > 0 } statuses) tickets = tickets.Where(t => statuses.Contains(t.Status));
        if (query.Priority is { } priority) tickets = tickets.Where(t => t.Priority == priority);
        if (query.CategoryId is { } categoryId) tickets = tickets.Where(t => t.CategoryId == categoryId);
        if (query.CustomerId is { } customerId) tickets = tickets.Where(t => t.CustomerId == customerId);
        if (query.Escalated is { } escalated) tickets = tickets.Where(t => t.IsEscalated == escalated);
        if (!string.IsNullOrWhiteSpace(query.Sla))
        {
            var now = DateTime.UtcNow;
            var open = TicketWorkflow.ActiveStatuses.ToList();
            tickets = query.Sla.Trim().ToLowerInvariant() switch
            {
                "breached" => tickets.Where(t => open.Contains(t.Status)
                    && ((t.FirstRespondedAt == null && t.FirstResponseDueAt < now) || t.ResolutionDueAt < now)),
                "atrisk" => tickets.Where(t => open.Contains(t.Status) && t.ResolutionAtRiskAt <= now && t.ResolutionDueAt >= now
                    && !(t.FirstRespondedAt == null && t.FirstResponseDueAt < now)),
                _ => throw new ValidationException([new ValidationFailure("Sla", "Use 'breached' or 'atRisk'.")]),
            };
        }

        switch (query.AssignedTo?.Trim().ToLowerInvariant())
        {
            case null or "": break;
            case "unassigned": tickets = tickets.Where(t => t.AssigneeId == null); break;
            case "me":
                var me = currentUser.UserId;
                tickets = tickets.Where(t => t.AssigneeId == me);
                break;
            default:
                if (!Guid.TryParse(query.AssignedTo, out var assigneeId))
                    throw new ValidationException([new ValidationFailure("AssignedTo", "Use 'me', 'unassigned' or a user id.")]);
                tickets = tickets.Where(t => t.AssigneeId == assigneeId);
                break;
        }

        var total = await tickets.CountAsync(ct);
        var items = await Sort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new TicketListItemDto(t.Id, t.Subject, t.CustomerId, t.Customer.Name, t.Category.Name,
                t.Priority, t.Status, t.AssigneeId,
                t.Assignee == null ? null : t.Assignee.FirstName + " " + t.Assignee.LastName,
                t.IsEscalated, t.CreatedAt, t.LastActivityAt,
                new TicketSlaDto(t.CreatedAt, t.FirstResponseDueAt, t.FirstRespondedAt, t.ResolutionDueAt, t.ResolvedAt ?? t.ClosedAt)))
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<TicketDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        await db.Tickets.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TicketDto(
                t.Id, t.Subject, t.Description,
                new TicketCustomerDto(t.Customer.Id, t.Customer.Name, t.Customer.Email, t.Customer.Phone, t.Customer.IsActive),
                t.CategoryId, t.Category.Name, t.Priority, t.Status, t.Channel,
                t.AssigneeId, t.Assignee == null ? null : t.Assignee.FirstName + " " + t.Assignee.LastName,
                t.IsEscalated, t.EscalatedAt, t.EscalationReason, t.ResolvedAt, t.ClosedAt,
                t.CreatedAt, db.Users.Where(u => u.Id == t.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
                t.LastActivityAt,
                new TicketSlaDto(t.CreatedAt, t.FirstResponseDueAt, t.FirstRespondedAt, t.ResolutionDueAt, t.ResolvedAt ?? t.ClosedAt)))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Ticket", id);

    public async Task<TicketDto> CreateAsync(CreateTicketRequest request, CancellationToken ct = default)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
                       ?? throw new ValidationException([new ValidationFailure("CustomerId", "Customer does not exist.")]);
        if (!customer.IsActive)
            throw new BusinessRuleException($"{customer.Name} is inactive. Reactivate the customer before opening tickets."); // T2
        await EnsureCategoryUsableAsync(request.CategoryId, currentCategoryId: null, ct);

        var assigneeId = request.AssigneeId;
        var assigneeName = assigneeId is null
            ? null
            : await AuthorizeAssignmentAsync(db, permissions, currentUser, null, assigneeId, ct);

        // Spec 007, S6 — hand unassigned work to the least-loaded agent when auto-assignment is on.
        string? assignedHow = null;
        if (assigneeId is null && await db.IsAutoAssignEnabledAsync(ct) && await db.PickLeastLoadedAgentAsync(ct) is { } pick)
        {
            (assigneeId, assigneeName) = (pick.Id, pick.Name);
            assignedHow = "Assigned automatically to the agent with the fewest active tickets.";
        }

        var now = DateTime.UtcNow;
        var ticket = new Ticket
        {
            CustomerId = request.CustomerId,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            CategoryId = request.CategoryId,
            Priority = request.Priority,
            Channel = request.Channel,
            AssigneeId = assigneeId,
            Status = assigneeId is null ? TicketStatus.New : TicketStatus.Open, // W4
            LastActivityAt = now,
        };
        await db.ApplyPolicyAsync(ticket, now, ct); // Spec 007, S1
        ticket.History.Add(History(TicketEventType.Created, to: ticket.Status.ToString()));
        if (assigneeName is not null) ticket.History.Add(History(TicketEventType.Assigned, to: assigneeName, message: assignedHow));

        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);

        if (assigneeId is { } who && who != currentUser.UserId) // S10
        {
            notifier.Notify(who, NotificationType.TicketAssigned, $"{Ticket.FormatCode(ticket.Id)} assigned to you",
                $"\"{ticket.Subject}\" ({ticket.Priority} priority).", ticket.Id);
            await db.SaveChangesAsync(ct);
        }
        return await GetByIdAsync(ticket.Id, ct);
    }

    public async Task<TicketDto> UpdateAsync(int id, UpdateTicketRequest request, CancellationToken ct = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var ticket = await db.Tickets.Include(t => t.Category).FirstOrDefaultAsync(t => t.Id == id, ct)
                     ?? throw new NotFoundException("Ticket", id);
        EnsureNotClosed(ticket);

        if (ticket.CategoryId != request.CategoryId)
        {
            var newCategory = await EnsureCategoryUsableAsync(request.CategoryId, ticket.CategoryId, ct);
            ticket.History.Add(History(TicketEventType.CategoryChanged, ticket.Category.Name, newCategory));
            ticket.CategoryId = request.CategoryId;
        }
        if (ticket.Priority != request.Priority)
        {
            ticket.History.Add(History(TicketEventType.PriorityChanged, ticket.Priority.ToString(), request.Priority.ToString()));
            ticket.Priority = request.Priority;
            await db.ApplyPolicyAsync(ticket, ticket.CreatedAt, ct); // Spec 007, S4
        }

        var changed = new List<string>();
        if (ticket.Subject != request.Subject.Trim()) changed.Add("subject");
        if (ticket.Description != request.Description.Trim()) changed.Add("description");
        if (ticket.Channel != request.Channel) changed.Add("channel");
        if (changed.Count > 0)
            ticket.History.Add(History(TicketEventType.Updated, message: $"Changed {string.Join(", ", changed)}."));

        ticket.Subject = request.Subject.Trim();
        ticket.Description = request.Description.Trim();
        ticket.Channel = request.Channel;
        ticket.LastActivityAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Ticket", id);
        // Spec 005, D6 — reminders about this ticket stay, just unlinked.
        await db.AgentTasks.Where(t => t.TicketId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.TicketId, (int?)null), ct);
        db.Tickets.Remove(ticket); // history cascades
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AssigneeDto>> GetAssigneesAsync(CancellationToken ct = default)
    {
        var active = TicketWorkflow.ActiveStatuses.ToList();
        return await db.Agents()
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new AssigneeDto(u.Id, u.FirstName + " " + u.LastName, u.Email!,
                db.Tickets.Count(t => t.AssigneeId == u.Id && active.Contains(t.Status))))
            .ToListAsync(ct);
    }

    /// <summary>T1 — the category must exist and be active, unless the ticket already uses it.</summary>
    private async Task<string> EnsureCategoryUsableAsync(int categoryId, int? currentCategoryId, CancellationToken ct)
    {
        var category = await db.TicketCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null || (!category.IsActive && category.Id != currentCategoryId))
            throw new ValidationException([new ValidationFailure("CategoryId", "Choose an active category.")]);
        return category.Name;
    }

    private static IQueryable<Ticket> Sort(IQueryable<Ticket> tickets, TicketListQuery q) =>
        (q.SortBy?.ToLowerInvariant(), q.IsDescending) switch
        {
            ("code", false) => tickets.OrderBy(t => t.Id),
            ("code", true) => tickets.OrderByDescending(t => t.Id),
            ("subject", false) => tickets.OrderBy(t => t.Subject),
            ("subject", true) => tickets.OrderByDescending(t => t.Subject),
            ("customer", false) => tickets.OrderBy(t => t.Customer.Name),
            ("customer", true) => tickets.OrderByDescending(t => t.Customer.Name),
            ("priority", false) => tickets.OrderBy(t => t.Priority).ThenByDescending(t => t.LastActivityAt),
            ("priority", true) => tickets.OrderByDescending(t => t.Priority).ThenByDescending(t => t.LastActivityAt),
            ("status", false) => tickets.OrderBy(t => t.Status).ThenByDescending(t => t.LastActivityAt),
            ("status", true) => tickets.OrderByDescending(t => t.Status).ThenByDescending(t => t.LastActivityAt),
            ("createdat", false) => tickets.OrderBy(t => t.CreatedAt),
            ("createdat", true) => tickets.OrderByDescending(t => t.CreatedAt),
            ("lastactivityat", false) => tickets.OrderBy(t => t.LastActivityAt),
            _ => tickets.OrderByDescending(t => t.LastActivityAt),
        };

    /// <summary>"TCK-00042", "tck42" or "42" → 42.</summary>
    private static int? ParseCode(string term)
    {
        var match = CodePattern().Match(term);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"^(?:TCK-?)?0*(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex CodePattern();
}
