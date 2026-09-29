using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Seeding;
using Portal.Infrastructure.Storage;

namespace Portal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services
            .AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = true;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<DatabaseInitializer>();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        return services;
    }
}
