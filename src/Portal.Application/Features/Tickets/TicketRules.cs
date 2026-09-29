using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Permissions;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Tickets;
using DomainPermissions = Portal.Domain.Authorization.Permissions;

namespace Portal.Application.Features.Tickets;

/// <summary>Rules and helpers shared by the ticket services.</summary>
internal static class TicketRules
{
    /// <summary>K4 — agents are active users whose role grants <c>Tickets.Work</c>.</summary>
    public static IQueryable<ApplicationUser> Agents(this IApplicationDbContext db) =>
        db.Users.Where(u => u.IsActive && u.UserRoles.Any(ur =>
            ur.Role.RolePermissions.Any(rp => rp.Permission.Key == DomainPermissions.Tickets.Work)));

    /// <summary>W5 — closed tickets are read-only until reopened.</summary>
    public static void EnsureNotClosed(Ticket ticket)
    {
        if (ticket.Status == TicketStatus.Closed)
            throw new BusinessRuleException($"{Ticket.FormatCode(ticket.Id)} is closed. Reopen it to make changes.");
    }

    /// <summary>A1–A3: who may set this assignee, and whether the assignee is an agent. Returns the assignee's name.</summary>
    public static async Task<string?> AuthorizeAssignmentAsync(
        IApplicationDbContext db, IPermissionService permissions, ICurrentUser currentUser,
        Guid? currentAssigneeId, Guid? newAssigneeId, CancellationToken ct)
    {
        if (!await permissions.CurrentUserHasAsync(currentUser, DomainPermissions.Tickets.Assign, ct))
        {
            var me = currentUser.UserId;
            var takingUnassigned = newAssigneeId == me && currentAssigneeId is null;
            var releasingOwn = newAssigneeId is null && currentAssigneeId == me;
            var keepingOwn = newAssigneeId == me && currentAssigneeId == me;
            if (!(takingUnassigned || releasingOwn || keepingOwn)
                || !await permissions.CurrentUserHasAsync(currentUser, DomainPermissions.Tickets.Work, ct))
                throw new ForbiddenException("You can only take unassigned tickets or release your own. Ask a supervisor to reassign.");
        }

        if (newAssigneeId is not { } id) return null;

        var name = await db.Agents().Where(u => u.Id == id).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);
        return name ?? throw new ValidationException([new ValidationFailure("AssigneeId", "The selected user is not an active agent.")]);
    }

    public static TicketHistoryEntry History(TicketEventType type, string? from = null, string? to = null, string? message = null) =>
        new() { Type = type, FromValue = from, ToValue = to, Message = message };
}
