using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Common.Security;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Customers;
using Portal.Application.Features.Permissions;
using Portal.Application.Features.Roles;
using Portal.Application.Features.Users;

namespace Portal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddSingleton<PermissionCache>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISessionRevoker, SessionRevoker>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<ICustomerActivityService, CustomerActivityService>();

        return services;
    }
}
