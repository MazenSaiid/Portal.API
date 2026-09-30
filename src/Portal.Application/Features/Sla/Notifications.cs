using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities.Sla;
using DomainPermissions = Portal.Domain.Authorization.Permissions;

namespace Portal.Application.Features.Sla;

/// <summary>
/// Queues in-app notifications (Spec 007, S9–S10). Rows are added to the caller's unit of work and saved with the
/// change that caused them, so a failed action never leaves a stray notification.
/// </summary>
public interface INotifier
{
    void Notify(Guid userId, NotificationType type, string title, string message, int? ticketId = null);
    Task NotifySupervisorsAsync(NotificationType type, string title, string message, int? ticketId, Guid? except, CancellationToken ct);
}

public sealed class Notifier(IApplicationDbContext db) : INotifier
{
    public void Notify(Guid userId, NotificationType type, string title, string message, int? ticketId = null) =>
        db.Notifications.Add(new Notification
        {
            UserId = userId, Type = type, Title = title, Message = message, TicketId = ticketId, CreatedAt = DateTime.UtcNow,
        });

    /// <summary>Supervisors = active users whose role grants <c>Tickets.Assign</c>.</summary>
    public async Task NotifySupervisorsAsync(NotificationType type, string title, string message, int? ticketId, Guid? except, CancellationToken ct)
    {
        var supervisors = await db.Users
            .Where(u => u.IsActive && u.Id != except && u.UserRoles.Any(ur =>
                ur.Role.RolePermissions.Any(rp => rp.Permission.Key == DomainPermissions.Tickets.Assign)))
            .Select(u => u.Id)
            .ToListAsync(ct);
        foreach (var id in supervisors) Notify(id, type, title, message, ticketId);
    }
}

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> GetMineAsync(bool unreadOnly, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(long id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}

/// <summary>Reading one's own notifications; everything is scoped to the signed-in user (SL4).</summary>
public sealed class NotificationService(IApplicationDbContext db, ICurrentUser currentUser) : INotificationService
{
    private const int Limit = 30;
    private IQueryable<Notification> Mine => db.Notifications.Where(n => n.UserId == currentUser.UserId);

    public async Task<IReadOnlyList<NotificationDto>> GetMineAsync(bool unreadOnly, CancellationToken ct = default) =>
        await Mine.AsNoTracking()
            .Where(n => !unreadOnly || !n.IsRead)
            .OrderByDescending(n => n.Id)
            .Take(Limit)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.TicketId, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);

    public Task<int> GetUnreadCountAsync(CancellationToken ct = default) => Mine.CountAsync(n => !n.IsRead, ct);

    public async Task MarkReadAsync(long id, CancellationToken ct = default)
    {
        var updated = await Mine.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        if (updated == 0) throw new NotFoundException("Notification", id);
    }

    public Task MarkAllReadAsync(CancellationToken ct = default) =>
        Mine.Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
}
