using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Common.Models;
using Portal.Application.Features.Tickets;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/tickets")]
public sealed class TicketsController(ITicketService tickets, ITicketWorkflowService workflow) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Tickets.View)]
    public Task<PagedResult<TicketListItemDto>> GetAll([FromQuery] TicketListQuery query, CancellationToken ct) =>
        tickets.GetPagedAsync(query, ct);

    /// <summary>Active agents who can be assigned (K4), with their number of active tickets.</summary>
    [HttpGet("assignees")]
    [HasPermission(Permissions.Tickets.View)]
    public Task<IReadOnlyList<AssigneeDto>> GetAssignees(CancellationToken ct) => tickets.GetAssigneesAsync(ct);

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Tickets.View)]
    public Task<TicketDto> GetById(int id, CancellationToken ct) => tickets.GetByIdAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Tickets.Create)]
    [ProducesResponseType<TicketDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var ticket = await tickets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Tickets.Edit)]
    public Task<TicketDto> Update(int id, UpdateTicketRequest request, CancellationToken ct) => tickets.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Tickets.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await tickets.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---------- Workflow ----------

    [HttpPost("{id:int}/status")]
    [HasPermission(Permissions.Tickets.Work)]
    public Task<TicketDto> ChangeStatus(int id, ChangeStatusRequest request, CancellationToken ct) =>
        workflow.ChangeStatusAsync(id, request, ct);

    /// <summary>Tickets.Assign may assign anyone; Tickets.Work may take or release their own (A1, A2).</summary>
    [HttpPost("{id:int}/assign")]
    [HasPermission(Permissions.Tickets.Assign, Permissions.Tickets.Work)]
    public Task<TicketDto> Assign(int id, AssignTicketRequest request, CancellationToken ct) =>
        workflow.AssignAsync(id, request, ct);

    [HttpPost("{id:int}/escalate")]
    [HasPermission(Permissions.Tickets.Escalate)]
    public Task<TicketDto> Escalate(int id, EscalateTicketRequest request, CancellationToken ct) =>
        workflow.EscalateAsync(id, request, ct);

    [HttpPost("{id:int}/de-escalate")]
    [HasPermission(Permissions.Tickets.Escalate)]
    public Task<TicketDto> DeEscalate(int id, DeEscalateTicketRequest request, CancellationToken ct) =>
        workflow.DeEscalateAsync(id, request, ct);

    [HttpPost("{id:int}/comments")]
    [HasPermission(Permissions.Tickets.Work)]
    [ProducesResponseType<TicketHistoryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketHistoryDto>> AddComment(int id, TicketCommentRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await workflow.AddCommentAsync(id, request, ct));

    [HttpGet("{id:int}/history")]
    [HasPermission(Permissions.Tickets.View)]
    public Task<IReadOnlyList<TicketHistoryDto>> GetHistory(int id, CancellationToken ct) => workflow.GetHistoryAsync(id, ct);
}
