using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Security;
using Portal.Application.Features.Permissions;
using Portal.Domain.Entities.Work;
using DomainPermissions = Portal.Domain.Authorization.Permissions;

namespace Portal.Application.Features.Work;

/// <summary>Shared and personal canned answers (D4, QR1, QR2).</summary>
public interface IQuickReplyService
{
    Task<IReadOnlyList<QuickReplyDto>> GetAvailableAsync(CancellationToken ct = default);
    Task<QuickReplyDto> CreateAsync(QuickReplyRequest request, CancellationToken ct = default);
    Task<QuickReplyDto> UpdateAsync(int id, QuickReplyRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class QuickReplyService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPermissionService permissions,
    IValidator<QuickReplyRequest> validator) : IQuickReplyService
{
    public async Task<IReadOnlyList<QuickReplyDto>> GetAvailableAsync(CancellationToken ct = default)
    {
        var me = currentUser.UserId;
        var canManageShared = await CanManageSharedAsync(ct);
        return await db.QuickReplies.AsNoTracking()
            .Where(q => q.OwnerId == null || q.OwnerId == me)
            .OrderBy(q => q.OwnerId != null).ThenBy(q => q.Title)
            .Select(q => new QuickReplyDto(q.Id, q.Title, q.Body, q.OwnerId == null, q.OwnerId == null ? canManageShared : true))
            .ToListAsync(ct);
    }

    public async Task<QuickReplyDto> CreateAsync(QuickReplyRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        if (request.IsShared) await EnsureCanManageSharedAsync(ct);

        var reply = new QuickReply
        {
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            OwnerId = request.IsShared ? null : currentUser.UserId,
        };
        db.QuickReplies.Add(reply);
        await db.SaveChangesAsync(ct);
        return new QuickReplyDto(reply.Id, reply.Title, reply.Body, request.IsShared, true);
    }

    public async Task<QuickReplyDto> UpdateAsync(int id, QuickReplyRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var reply = await LoadVisibleAsync(id, ct);
        var wasShared = reply.OwnerId is null;
        // Touching a shared reply, or moving a reply between shared and personal, needs the manage permission.
        if (wasShared || request.IsShared) await EnsureCanManageSharedAsync(ct);

        reply.Title = request.Title.Trim();
        reply.Body = request.Body.Trim();
        reply.OwnerId = request.IsShared ? null : currentUser.UserId;
        await db.SaveChangesAsync(ct);
        return new QuickReplyDto(reply.Id, reply.Title, reply.Body, request.IsShared, true);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var reply = await LoadVisibleAsync(id, ct);
        if (reply.OwnerId is null) await EnsureCanManageSharedAsync(ct);
        db.QuickReplies.Remove(reply);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Shared replies and my own; other people's personal replies are "not found" (QR2).</summary>
    private async Task<QuickReply> LoadVisibleAsync(int id, CancellationToken ct)
    {
        var me = currentUser.UserId;
        return await db.QuickReplies.FirstOrDefaultAsync(q => q.Id == id && (q.OwnerId == null || q.OwnerId == me), ct)
               ?? throw new NotFoundException("Quick reply", id);
    }

    private Task<bool> CanManageSharedAsync(CancellationToken ct) =>
        permissions.CurrentUserHasAsync(currentUser, DomainPermissions.QuickReplies.Manage, ct);

    private async Task EnsureCanManageSharedAsync(CancellationToken ct)
    {
        if (!await CanManageSharedAsync(ct))
            throw new ForbiddenException("Only quick-reply managers can change shared replies. Save it as a personal reply instead.");
    }
}
