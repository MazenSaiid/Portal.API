using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Models;
using Portal.Domain.Entities.Auditing;

namespace Portal.Application.Features.Auditing;

/// <summary>Writes security events that aren't plain data changes (Spec 006, L5).</summary>
public interface IAuditLogger
{
    /// <param name="userId">Override for events where nobody is signed in yet (e.g. a sign-in attempt).</param>
    Task LogAsync(AuditAction action, string summary, Guid? userId = null, string? userName = null,
        string entityType = "Security", string? entityId = null, CancellationToken ct = default);
}

public sealed class AuditLogger(IApplicationDbContext db, ICurrentUser currentUser) : IAuditLogger
{
    public async Task LogAsync(AuditAction action, string summary, Guid? userId = null, string? userName = null,
        string entityType = "Security", string? entityId = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = DateTime.UtcNow,
            UserId = userId ?? currentUser.UserId,
            UserName = userName ?? currentUser.UserName,
            IpAddress = currentUser.IpAddress,
            Action = action,
            EntityType = entityType,
            EntityId = entityId ?? (userId ?? currentUser.UserId)?.ToString(),
            Summary = summary,
        });
        await db.SaveChangesAsync(ct);
    }
}

// ---------- Reading ----------

public sealed class AuditLogQuery : PagedQuery
{
    public string? Search { get; set; }
    public AuditAction? Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed record AuditLogDto(
    long Id,
    DateTime OccurredAt,
    Guid? UserId,
    string? UserName,
    string? IpAddress,
    AuditAction Action,
    string EntityType,
    string? EntityId,
    string Summary,
    IReadOnlyList<AuditChange> Changes);

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditLogQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetEntityTypesAsync(CancellationToken ct = default);
}

public sealed class AuditLogService(IApplicationDbContext db) : IAuditLogService
{
    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var logs = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            logs = logs.Where(l => l.Summary.Contains(term) || (l.UserName != null && l.UserName.Contains(term))
                                   || (l.IpAddress != null && l.IpAddress == term) || (l.EntityId != null && l.EntityId == term));
        }
        if (query.Action is { } action) logs = logs.Where(l => l.Action == action);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) logs = logs.Where(l => l.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) logs = logs.Where(l => l.EntityId == query.EntityId);
        if (query.UserId is { } userId) logs = logs.Where(l => l.UserId == userId);
        if (query.From is { } from) { var f = from.ToUniversalTime(); logs = logs.Where(l => l.OccurredAt >= f); }
        if (query.To is { } to) { var t = to.ToUniversalTime(); logs = logs.Where(l => l.OccurredAt <= t); }

        var total = await logs.CountAsync(ct);
        var rows = await logs
            .OrderByDescending(l => l.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = rows.Select(l => new AuditLogDto(l.Id, l.OccurredAt, l.UserId, l.UserName, l.IpAddress, l.Action,
            l.EntityType, l.EntityId, l.Summary,
            l.Changes is null ? [] : JsonSerializer.Deserialize<List<AuditChange>>(l.Changes) ?? [])).ToList();

        return new PagedResult<AuditLogDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<string>> GetEntityTypesAsync(CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking().Select(l => l.EntityType).Distinct().OrderBy(t => t).ToListAsync(ct);
}
