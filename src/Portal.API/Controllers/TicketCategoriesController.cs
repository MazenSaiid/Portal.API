using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Features.Tickets;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/ticket-categories")]
public sealed class TicketCategoriesController(ITicketCategoryService categories) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Tickets.View, Permissions.Tickets.ManageCategories)]
    public Task<IReadOnlyList<TicketCategoryDto>> GetAll([FromQuery] bool activeOnly, CancellationToken ct) =>
        categories.GetAllAsync(activeOnly, ct);

    [HttpPost]
    [HasPermission(Permissions.Tickets.ManageCategories)]
    [ProducesResponseType<TicketCategoryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketCategoryDto>> Create(TicketCategoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await categories.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Tickets.ManageCategories)]
    public Task<TicketCategoryDto> Update(int id, TicketCategoryRequest request, CancellationToken ct) =>
        categories.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Tickets.ManageCategories)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}
