using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Models;
using Portal.Application.Features.Permissions;
using DomainPermissions = Portal.Domain.Authorization.Permissions;
using Portal.Domain.Entities.Customers;

namespace Portal.Application.Features.Customers;

/// <summary>Interaction history, notes and attachments of a customer.</summary>
public interface ICustomerActivityService
{
    Task<PagedResult<InteractionDto>> GetInteractionsAsync(int customerId, InteractionQuery query, CancellationToken ct = default);
    Task<InteractionDto> LogInteractionAsync(int customerId, LogInteractionRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<NoteDto>> GetNotesAsync(int customerId, CancellationToken ct = default);
    Task<NoteDto> AddNoteAsync(int customerId, NoteRequest request, CancellationToken ct = default);
    Task<NoteDto> UpdateNoteAsync(int customerId, Guid noteId, NoteRequest request, CancellationToken ct = default);
    Task DeleteNoteAsync(int customerId, Guid noteId, CancellationToken ct = default);

    Task<IReadOnlyList<AttachmentDto>> GetAttachmentsAsync(int customerId, CancellationToken ct = default);
    Task<AttachmentDto> UploadAttachmentAsync(int customerId, UploadedFile file, CancellationToken ct = default);
    Task<FileDownload> DownloadAttachmentAsync(int customerId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAttachmentAsync(int customerId, Guid attachmentId, CancellationToken ct = default);
}

public sealed class CustomerActivityService(
    IApplicationDbContext db,
    IFileStorage storage,
    ICurrentUser currentUser,
    IPermissionService permissionService,
    IValidator<LogInteractionRequest> interactionValidator,
    IValidator<NoteRequest> noteValidator) : ICustomerActivityService
{
    // ---------- Interactions (immutable, C5) ----------

    public async Task<PagedResult<InteractionDto>> GetInteractionsAsync(int customerId, InteractionQuery query, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);
        var interactions = db.CustomerInteractions.AsNoTracking().Where(i => i.CustomerId == customerId);

        var total = await interactions.CountAsync(ct);
        var items = await interactions
            .OrderByDescending(i => i.OccurredAt).ThenByDescending(i => i.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new InteractionDto(i.Id, i.Type, i.Direction, i.Subject, i.Summary, i.OccurredAt, i.CreatedAt,
                db.Users.Where(u => u.Id == i.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault()))
            .ToListAsync(ct);

        return new PagedResult<InteractionDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<InteractionDto> LogInteractionAsync(int customerId, LogInteractionRequest request, CancellationToken ct = default)
    {
        await interactionValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCustomerExistsAsync(customerId, ct);

        var interaction = new CustomerInteraction
        {
            CustomerId = customerId,
            Type = request.Type,
            Direction = request.Direction,
            Subject = request.Subject.Trim(),
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            OccurredAt = request.OccurredAt?.ToUniversalTime() ?? DateTime.UtcNow,
        };
        db.CustomerInteractions.Add(interaction);
        await db.SaveChangesAsync(ct);

        return new InteractionDto(interaction.Id, interaction.Type, interaction.Direction, interaction.Subject,
            interaction.Summary, interaction.OccurredAt, interaction.CreatedAt, await CurrentUserNameAsync(ct));
    }

    // ---------- Notes ----------

    public async Task<IReadOnlyList<NoteDto>> GetNotesAsync(int customerId, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);
        var canManageAll = await CanManageAllAsync(ct);
        var me = currentUser.UserId;

        return await db.CustomerNotes.AsNoTracking()
            .Where(n => n.CustomerId == customerId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteDto(n.Id, n.Content, n.CreatedAt,
                db.Users.Where(u => u.Id == n.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
                n.UpdatedAt, canManageAll || n.CreatedById == me))
            .ToListAsync(ct);
    }

    public async Task<NoteDto> AddNoteAsync(int customerId, NoteRequest request, CancellationToken ct = default)
    {
        await noteValidator.ValidateAndThrowAsync(request, ct);
        await EnsureCustomerExistsAsync(customerId, ct);

        var note = new CustomerNote { CustomerId = customerId, Content = request.Content.Trim() };
        db.CustomerNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return new NoteDto(note.Id, note.Content, note.CreatedAt, await CurrentUserNameAsync(ct), null, CanManage: true);
    }

    public async Task<NoteDto> UpdateNoteAsync(int customerId, Guid noteId, NoteRequest request, CancellationToken ct = default)
    {
        await noteValidator.ValidateAndThrowAsync(request, ct);
        var note = await db.CustomerNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.CustomerId == customerId, ct)
                   ?? throw new NotFoundException("Note", noteId);
        await EnsureCanManageAsync(note.CreatedById, "note", ct);

        note.Content = request.Content.Trim();
        await db.SaveChangesAsync(ct);

        var author = await db.Users.Where(u => u.Id == note.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);
        return new NoteDto(note.Id, note.Content, note.CreatedAt, author, note.UpdatedAt, CanManage: true);
    }

    public async Task DeleteNoteAsync(int customerId, Guid noteId, CancellationToken ct = default)
    {
        var note = await db.CustomerNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.CustomerId == customerId, ct)
                   ?? throw new NotFoundException("Note", noteId);
        await EnsureCanManageAsync(note.CreatedById, "note", ct);

        db.CustomerNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Attachments ----------

    public async Task<IReadOnlyList<AttachmentDto>> GetAttachmentsAsync(int customerId, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);
        var canManageAll = await CanManageAllAsync(ct);
        var me = currentUser.UserId;

        return await db.CustomerAttachments.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt,
                db.Users.Where(u => u.Id == a.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
                canManageAll || a.CreatedById == me))
            .ToListAsync(ct);
    }

    public async Task<AttachmentDto> UploadAttachmentAsync(int customerId, UploadedFile file, CancellationToken ct = default)
    {
        var (fileName, extension, contentType) = AttachmentRules.Validate(file);
        await EnsureCustomerExistsAsync(customerId, ct);

        var key = await storage.SaveAsync(file.Content, extension, ct);
        var attachment = new CustomerAttachment
        {
            CustomerId = customerId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = file.Length,
            StorageKey = key,
        };

        try
        {
            db.CustomerAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None); // don't leave an orphaned file behind
            throw;
        }

        return new AttachmentDto(attachment.Id, attachment.FileName, attachment.ContentType, attachment.SizeBytes,
            attachment.CreatedAt, await CurrentUserNameAsync(ct), CanManage: true);
    }

    public async Task<FileDownload> DownloadAttachmentAsync(int customerId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await db.CustomerAttachments.AsNoTracking()
                             .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CustomerId == customerId, ct)
                         ?? throw new NotFoundException("Attachment", attachmentId);

        var stream = await storage.OpenReadAsync(attachment.StorageKey, ct)
                     ?? throw new NotFoundException("Attachment file", attachmentId);
        return new FileDownload(stream, attachment.FileName, attachment.ContentType);
    }

    public async Task DeleteAttachmentAsync(int customerId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await db.CustomerAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.CustomerId == customerId, ct)
                         ?? throw new NotFoundException("Attachment", attachmentId);
        await EnsureCanManageAsync(attachment.CreatedById, "file", ct);

        db.CustomerAttachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(attachment.StorageKey, ct);
    }

    // ---------- helpers ----------

    private async Task EnsureCustomerExistsAsync(int customerId, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            throw new NotFoundException("Customer", customerId);
    }

    private async Task<bool> CanManageAllAsync(CancellationToken ct) =>
        currentUser.UserId is { } userId &&
        (await permissionService.GetUserPermissionsAsync(userId, ct)).Contains(DomainPermissions.Customers.Edit);

    /// <summary>CR7 — authors manage their own items; <c>Customers.Edit</c> holders manage everyone's.</summary>
    private async Task EnsureCanManageAsync(Guid? authorId, string what, CancellationToken ct)
    {
        if (authorId is not null && authorId == currentUser.UserId) return;
        if (await CanManageAllAsync(ct)) return;
        throw new ForbiddenException($"Only the author of this {what} or a customer editor can change it.");
    }

    private Task<string?> CurrentUserNameAsync(CancellationToken ct) =>
        db.Users.Where(u => u.Id == currentUser.UserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefaultAsync(ct);
}
