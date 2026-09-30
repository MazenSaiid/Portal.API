using System.Linq.Expressions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities.Work;

namespace Portal.Application.Features.Work;

/// <summary>Private to-dos of the signed-in user. Every query is scoped to the owner (DT2).</summary>
public interface ITaskService
{
    Task<IReadOnlyList<TaskDto>> GetMineAsync(TaskQuery query, int? take = null, CancellationToken ct = default);
    Task<RemindersDto> GetRemindersAsync(DateTime? endOfDayUtc, CancellationToken ct = default);
    Task<TaskDto> CreateAsync(TaskRequest request, CancellationToken ct = default);
    Task<TaskDto> UpdateAsync(Guid id, TaskRequest request, CancellationToken ct = default);
    Task<TaskDto> SetDoneAsync(Guid id, bool isDone, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class TaskService(IApplicationDbContext db, ICurrentUser currentUser, IValidator<TaskRequest> validator) : ITaskService
{
    private static readonly Expression<Func<AgentTask, TaskDto>> ToDto = t => new TaskDto(
        t.Id, t.Title, t.Notes, t.DueAt, t.IsDone, t.CompletedAt,
        t.TicketId, t.Ticket == null ? null : t.Ticket.Subject,
        t.CustomerId, t.Customer == null ? null : t.Customer.Name,
        t.CreatedAt);

    private Guid Me => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    private IQueryable<AgentTask> Mine => db.AgentTasks.Where(t => t.OwnerId == Me);

    public async Task<IReadOnlyList<TaskDto>> GetMineAsync(TaskQuery query, int? take = null, CancellationToken ct = default)
    {
        IQueryable<AgentTask> tasks = (query.Status?.ToLowerInvariant()) switch
        {
            "done" => Mine.Where(t => t.IsDone).OrderByDescending(t => t.CompletedAt),
            "all" => Mine.OrderBy(t => t.IsDone).ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.CreatedAt),
            // Open: dated tasks first (so overdue ones come first), then undated, newest first.
            _ => Mine.Where(t => !t.IsDone).OrderBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.CreatedAt),
        };
        if (take is { } n) tasks = tasks.Take(n);
        return await tasks.AsNoTracking().Select(ToDto).ToListAsync(ct);
    }

    /// <summary>D3 — overdue and due before the end of the user's day (the client sends its local end of day in UTC).</summary>
    public async Task<RemindersDto> GetRemindersAsync(DateTime? endOfDayUtc, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var endOfDay = endOfDayUtc?.ToUniversalTime() ?? now.Date.AddDays(1);
        if (endOfDay < now) endOfDay = now;

        var open = Mine.Where(t => !t.IsDone && t.DueAt != null);
        var overdue = await open.CountAsync(t => t.DueAt < now, ct);
        var dueToday = await open.CountAsync(t => t.DueAt >= now && t.DueAt <= endOfDay, ct);
        return new RemindersDto(overdue, dueToday);
    }

    public async Task<TaskDto> CreateAsync(TaskRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var task = new AgentTask { OwnerId = Me };
        await ApplyAsync(task, request, ct);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return await GetAsync(task.Id, ct);
    }

    public async Task<TaskDto> UpdateAsync(Guid id, TaskRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var task = await LoadAsync(id, ct);
        await ApplyAsync(task, request, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TaskDto> SetDoneAsync(Guid id, bool isDone, CancellationToken ct = default)
    {
        var task = await LoadAsync(id, ct);
        task.IsDone = isDone;
        task.CompletedAt = isDone ? DateTime.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        db.AgentTasks.Remove(await LoadAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(AgentTask task, TaskRequest request, CancellationToken ct)
    {
        var customerId = request.CustomerId;
        if (request.TicketId is { } ticketId)
        {
            var ticketCustomer = await db.Tickets.Where(t => t.Id == ticketId).Select(t => (int?)t.CustomerId).FirstOrDefaultAsync(ct)
                                 ?? throw new ValidationException([new ValidationFailure("TicketId", "Ticket does not exist.")]);
            customerId ??= ticketCustomer; // a ticket reminder is also about its customer
        }
        if (customerId is { } cid && !await db.Customers.AnyAsync(c => c.Id == cid, ct))
            throw new ValidationException([new ValidationFailure("CustomerId", "Customer does not exist.")]);

        task.Title = request.Title.Trim();
        task.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        task.DueAt = request.DueAt?.ToUniversalTime();
        task.TicketId = request.TicketId;
        task.CustomerId = customerId;
    }

    /// <summary>DT2 — someone else's task is reported as not found.</summary>
    private async Task<AgentTask> LoadAsync(Guid id, CancellationToken ct) =>
        await Mine.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Task", id);

    private async Task<TaskDto> GetAsync(Guid id, CancellationToken ct) =>
        await Mine.AsNoTracking().Where(t => t.Id == id).Select(ToDto).FirstAsync(ct);
}
