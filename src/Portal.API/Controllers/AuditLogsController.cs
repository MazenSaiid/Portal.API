using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Common.Models;
using Portal.Application.Features.Auditing;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

/// <summary>Read-only by design: there are no endpoints to change or delete audit entries (Spec 006, L6).</summary>
[ApiController]
[Route("api/audit-logs")]
[HasPermission(Permissions.AuditLogs.View)]
public sealed class AuditLogsController(IAuditLogService auditLogs) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditLogDto>> GetAll([FromQuery] AuditLogQuery query, CancellationToken ct) =>
        auditLogs.GetPagedAsync(query, ct);

    [HttpGet("entity-types")]
    public Task<IReadOnlyList<string>> GetEntityTypes(CancellationToken ct) => auditLogs.GetEntityTypesAsync(ct);
}
