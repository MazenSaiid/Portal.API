using Portal.Application.Features.Permissions;

namespace Portal.Application.Features.Roles;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int UserCount,
    int PermissionCount,
    DateTime CreatedAt);

public sealed record RoleLookupDto(Guid Id, string Name);

public sealed record RolePermissionsDto(
    Guid RoleId,
    string RoleName,
    bool IsSystem,
    IReadOnlyList<PermissionModuleDto> Modules);

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<int>? PermissionIds);

public sealed record UpdateRoleRequest(string Name, string? Description);

/// <summary>Grants or revokes a set of permissions — one id for a single toggle, many for a whole module.</summary>
public sealed record SetRolePermissionsRequest(IReadOnlyList<int> PermissionIds, bool IsGranted);
