using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Features.Roles;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/roles")]
public sealed class RolesController(IRoleService roleService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    public Task<IReadOnlyList<RoleDto>> GetAll([FromQuery] string? search, CancellationToken ct) =>
        roleService.GetAllAsync(search, ct);

    /// <summary>Id/name pairs for dropdowns (e.g. the user form).</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.Users.View, Permissions.Roles.View)]
    public Task<IReadOnlyList<RoleLookupDto>> Lookup(CancellationToken ct) => roleService.GetLookupAsync(ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Roles.View)]
    public Task<RoleDto> GetById(Guid id, CancellationToken ct) => roleService.GetByIdAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Roles.Create)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleRequest request, CancellationToken ct)
    {
        var role = await roleService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = role.Id }, role);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Roles.Edit)]
    public Task<RoleDto> Update(Guid id, UpdateRoleRequest request, CancellationToken ct) =>
        roleService.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Roles.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await roleService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/permissions")]
    [HasPermission(Permissions.Roles.View)]
    public Task<RolePermissionsDto> GetPermissions(Guid id, CancellationToken ct) =>
        roleService.GetPermissionsAsync(id, ct);

    /// <summary>Grants or revokes permissions. Send one id for a single toggle, or a module's ids to toggle it all.</summary>
    [HttpPut("{id:guid}/permissions")]
    [HasPermission(Permissions.Roles.ManagePermissions)]
    public Task<RolePermissionsDto> SetPermissions(Guid id, SetRolePermissionsRequest request, CancellationToken ct) =>
        roleService.SetPermissionsAsync(id, request, ct);
}
