using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Common.Models;
using Portal.Application.Features.Users;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public Task<PagedResult<UserDto>> GetAll([FromQuery] UserListQuery query, CancellationToken ct) =>
        userService.GetPagedAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.View)]
    public Task<UserDto> GetById(Guid id, CancellationToken ct) => userService.GetByIdAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Users.Create)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await userService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Edit)]
    public Task<UserDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        userService.UpdateAsync(id, request, ct);

    [HttpPatch("{id:guid}/status")]
    [HasPermission(Permissions.Users.Edit)]
    public Task<UserDto> SetStatus(Guid id, UpdateUserStatusRequest request, CancellationToken ct) =>
        userService.SetStatusAsync(id, request.IsActive, ct);

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Permissions.Users.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await userService.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Users.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await userService.DeleteAsync(id, ct);
        return NoContent();
    }
}
