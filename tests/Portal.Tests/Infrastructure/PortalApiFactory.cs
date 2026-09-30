using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portal.Infrastructure.Persistence;

namespace Portal.Tests.Infrastructure;

/// <summary>
/// Runs the real API in-process against an isolated in-memory SQLite database.
/// One database per factory instance (i.e. per test class).
/// </summary>
public sealed class PortalApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin@12345";

    // Kept open for the factory's lifetime; an in-memory SQLite database lives as long as its connection.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    /// <summary>Uploaded files go to a throw-away folder per factory.</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "portal-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "1000");
        builder.UseSetting("RateLimiting:RefreshPermitsPerMinute", "1000");
        builder.UseSetting("Storage:RootPath", StorageRoot);
        builder.UseSetting("Sla:MonitorEnabled", "false"); // tests run the SLA engine explicitly

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            // TestServer has no socket, so give requests a client IP like Kestrel would (audit logs record it).
            services.AddSingleton<IStartupFilter, LoopbackClientIpFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        _connection.Dispose();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
    }
}

internal sealed class LoopbackClientIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
            return nextMiddleware();
        });
        next(app);
    };
}
