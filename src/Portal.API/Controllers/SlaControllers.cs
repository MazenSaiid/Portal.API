using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Features.Sla;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/sla")]
public sealed class SlaController(ISlaService sla) : ControllerBase
{
    [HttpGet("policies")]
    [HasPermission(Permissions.Tickets.View, Permissions.Sla.Manage)]
    public Task<IReadOnlyList<SlaPolicyDto>> GetPolicies(CancellationToken ct) => sla.GetPoliciesAsync(ct);

    [HttpPut("policies")]
    [HasPermission(Permissions.Sla.Manage)]
    public Task<IReadOnlyList<SlaPolicyDto>> UpdatePolicies(List<SlaPolicyDto> policies, CancellationToken ct) =>
        sla.UpdatePoliciesAsync(policies, ct);

    [HttpGet("settings")]
    [HasPermission(Permissions.Sla.Manage)]
    public Task<AutomationSettingsDto> GetSettings(CancellationToken ct) => sla.GetSettingsAsync(ct);

    [HttpPut("settings")]
    [HasPermission(Permissions.Sla.Manage)]
    public Task<AutomationSettingsDto> UpdateSettings(AutomationSettingsDto settings, CancellationToken ct) => sla.UpdateSettingsAsync(settings, ct);

    [HttpGet("rules")]
    [HasPermission(Permissions.Sla.Manage)]
    public Task<IReadOnlyList<EscalationRuleDto>> GetRules(CancellationToken ct) => sla.GetRulesAsync(ct);

    [HttpPost("rules")]
    [HasPermission(Permissions.Sla.Manage)]
    [ProducesResponseType<EscalationRuleDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<EscalationRuleDto>> CreateRule(EscalationRuleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await sla.CreateRuleAsync(request, ct));

    [HttpPut("rules/{id:int}")]
    [HasPermission(Permissions.Sla.Manage)]
    public Task<EscalationRuleDto> UpdateRule(int id, EscalationRuleRequest request, CancellationToken ct) => sla.UpdateRuleAsync(id, request, ct);

    [HttpDelete("rules/{id:int}")]
    [HasPermission(Permissions.Sla.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteRule(int id, CancellationToken ct)
    {
        await sla.DeleteRuleAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Every signed-in user reads only their own notifications (Spec 007, SL4).</summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<NotificationDto>> GetMine([FromQuery] bool unreadOnly, CancellationToken ct) =>
        notifications.GetMineAsync(unreadOnly, ct);

    [HttpGet("unread-count")]
    public async Task<object> UnreadCount(CancellationToken ct) => new { count = await notifications.GetUnreadCountAsync(ct) };

    [HttpPost("{id:long}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
