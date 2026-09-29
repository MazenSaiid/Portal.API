using Portal.Application.Common.Interfaces;
using Portal.Application.Features.Permissions;

namespace Portal.Application.Common.Security;

/// <summary>For rules that depend on who is asking, beyond the endpoint's own permission check.</summary>
public static class PermissionChecks
{
    public static async Task<bool> CurrentUserHasAsync(
        this IPermissionService permissions, ICurrentUser currentUser, string permission, CancellationToken ct = default) =>
        currentUser.UserId is { } userId && (await permissions.GetUserPermissionsAsync(userId, ct)).Contains(permission);
}
