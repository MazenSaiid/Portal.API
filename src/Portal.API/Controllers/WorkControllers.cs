using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Features.Work;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(IDashboardService dashboard) : ControllerBase
{
    /// <param name="endOfDay">The user's local end of day (as UTC), used for "due today" reminders.</param>
    [HttpGet]
    [HasPermission(Permissions.Dashboard.View)]
    public Task<AgentDashboardDto> Get([FromQuery] DateTime? endOfDay, CancellationToken ct) => dashboard.GetAsync(endOfDay, ct);
}

[ApiController]
[Route("api/tasks")]
[HasPermission(Permissions.Dashboard.View)]
public sealed class TasksController(ITaskService tasks) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TaskDto>> GetMine([FromQuery] TaskQuery query, CancellationToken ct) => tasks.GetMineAsync(query, ct: ct);

    [HttpGet("reminders")]
    public Task<RemindersDto> GetReminders([FromQuery] DateTime? endOfDay, CancellationToken ct) => tasks.GetRemindersAsync(endOfDay, ct);

    [HttpPost]
    [ProducesResponseType<TaskDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TaskDto>> Create(TaskRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await tasks.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<TaskDto> Update(Guid id, TaskRequest request, CancellationToken ct) => tasks.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/complete")]
    public Task<TaskDto> Complete(Guid id, CompleteTaskRequest request, CancellationToken ct) => tasks.SetDoneAsync(id, request.IsDone, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tasks.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/quick-replies")]
[HasPermission(Permissions.Tickets.Work, Permissions.QuickReplies.Manage)]
public sealed class QuickRepliesController(IQuickReplyService replies) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<QuickReplyDto>> GetAll(CancellationToken ct) => replies.GetAvailableAsync(ct);

    [HttpPost]
    [ProducesResponseType<QuickReplyDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<QuickReplyDto>> Create(QuickReplyRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await replies.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public Task<QuickReplyDto> Update(int id, QuickReplyRequest request, CancellationToken ct) => replies.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await replies.DeleteAsync(id, ct);
        return NoContent();
    }
}
