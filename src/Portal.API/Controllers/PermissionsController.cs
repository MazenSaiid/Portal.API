using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Features.Permissions;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/permissions")]
public sealed class PermissionsController(IPermissionService permissionService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    public Task<IReadOnlyList<PermissionModuleDto>> GetCatalog(CancellationToken ct) =>
        permissionService.GetCatalogAsync(ct);
}
