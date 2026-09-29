using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Portal.API.Infrastructure;
using Portal.Application.Features.Permissions;

namespace Portal.API.Authorization;

/// <summary>
/// Protects an endpoint with one or more permission keys. When several keys are given,
/// holding <b>any</b> of them is enough.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(params string[] permissions)
    : AuthorizeAttribute(PermissionPolicy.Prefix + string.Join(PermissionPolicy.Separator, permissions));

public static class PermissionPolicy
{
    public const string Prefix = "Permission:";
    public const char Separator = '|';
}

public sealed class PermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Permissions { get; } = permissions;
}

/// <summary>Builds permission policies on demand, so no policy has to be registered per permission.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var permissions = policyName[PermissionPolicy.Prefix.Length..]
            .Split(PermissionPolicy.Separator, StringSplitOptions.RemoveEmptyEntries);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();
    }
}

/// <summary>Registered as scoped so it can use the request's <see cref="IPermissionService"/>.</summary>
public sealed class PermissionAuthorizationHandler(IPermissionService permissionService)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.GetUserId() is not { } userId)
            return;

        var permissions = await permissionService.GetUserPermissionsAsync(userId);

        if (requirement.Permissions.Any(permissions.Contains))
            context.Succeed(requirement);
    }
}
