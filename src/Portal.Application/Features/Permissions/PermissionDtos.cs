namespace Portal.Application.Features.Permissions;

public sealed record PermissionItemDto(int Id, string Key, string Description, bool IsGranted);

public sealed record PermissionModuleDto(string Module, IReadOnlyList<PermissionItemDto> Permissions);
