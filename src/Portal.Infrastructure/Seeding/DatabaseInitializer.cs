using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Domain.Authorization;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Tickets;
using Portal.Domain.Entities.Work;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; init; } = "admin@portal.local";

    /// <summary>When empty the admin user is not created (e.g. production after first run).</summary>
    public string? AdminPassword { get; init; }
}

/// <summary>
/// Brings the database to the expected state on startup: applies migrations, syncs the
/// permission catalogue from <see cref="PermissionRegistry"/>, and seeds default roles and the admin user.
/// Every step is idempotent.
/// </summary>
public sealed class DatabaseInitializer(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<SeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (db.Database.IsSqlServer())
            await db.Database.MigrateAsync(ct);
        else
            await db.Database.EnsureCreatedAsync(ct);

        await SyncPermissionsAsync(ct);
        var admin = await EnsureRoleAsync(SystemRoles.Administrator, "Full access to every module.", isSystem: true);
        await EnsureRoleAsync("Agent", "Support agent. Permissions are granted per module.", isSystem: false);
        await GrantAllPermissionsAsync(admin, ct);
        await EnsureAdminUserAsync();
        await SeedTicketCategoriesAsync(ct);
        await SeedQuickRepliesAsync(ct);
    }

    /// <summary>Starter shared replies on an empty database only.</summary>
    private async Task SeedQuickRepliesAsync(CancellationToken ct)
    {
        if (await db.QuickReplies.AnyAsync(ct)) return;
        db.QuickReplies.AddRange(
            new QuickReply { Title = "Acknowledge request", Body = "Hello {customer},\n\nThank you for contacting us. We have logged your request as {ticket} and will get back to you shortly.\n\nBest regards,\n{agent}" },
            new QuickReply { Title = "Ask for more details", Body = "Hello {customer},\n\nTo help us resolve {ticket} quickly, could you share any screenshots, reference numbers or the exact steps you took?\n\nThanks,\n{agent}" },
            new QuickReply { Title = "Confirm resolution", Body = "Hello {customer},\n\nWe believe {ticket} is now resolved. If anything still isn't right, just reply and we'll reopen it.\n\nKind regards,\n{agent}" });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Starter categories on an empty database only; afterwards admins own the list.</summary>
    private async Task SeedTicketCategoriesAsync(CancellationToken ct)
    {
        if (await db.TicketCategories.AnyAsync(ct)) return;
        db.TicketCategories.AddRange(
            new TicketCategory { Name = "General", Description = "Questions and requests that fit nowhere else" },
            new TicketCategory { Name = "Billing", Description = "Invoices, payments and refunds" },
            new TicketCategory { Name = "Technical", Description = "Product problems and outages" },
            new TicketCategory { Name = "Account", Description = "Access, profile and account changes" },
            new TicketCategory { Name = "Complaint", Description = "Service complaints and escalations from customers" });
        await db.SaveChangesAsync(ct);
    }

    private async Task SyncPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Key, ct);
        var definitions = PermissionRegistry.All;

        for (var i = 0; i < definitions.Count; i++)
        {
            var d = definitions[i];
            if (existing.Remove(d.Key, out var permission))
            {
                permission.Module = d.Module;
                permission.Description = d.Description;
                permission.SortOrder = i;
            }
            else
            {
                db.Permissions.Add(new Permission { Key = d.Key, Module = d.Module, Description = d.Description, SortOrder = i });
                logger.LogInformation("Added permission {Permission}", d.Key);
            }
        }

        // Whatever is left is no longer defined in code; its grants are removed by cascade.
        foreach (var obsolete in existing.Values)
        {
            db.Permissions.Remove(obsolete);
            logger.LogInformation("Removed obsolete permission {Permission}", obsolete.Key);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<ApplicationRole> EnsureRoleAsync(string name, string description, bool isSystem)
    {
        var role = await roleManager.FindByNameAsync(name);
        if (role is not null) return role;

        role = new ApplicationRole { Name = name, Description = description, IsSystem = isSystem };
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed role {name}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        return role;
    }

    private async Task GrantAllPermissionsAsync(ApplicationRole role, CancellationToken ct)
    {
        var granted = await db.RolePermissions.Where(rp => rp.RoleId == role.Id).Select(rp => rp.PermissionId).ToListAsync(ct);
        var missing = await db.Permissions.Where(p => !granted.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        if (missing.Count == 0) return;

        db.RolePermissions.AddRange(missing.Select(id => new RolePermission { RoleId = role.Id, PermissionId = id }));
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureAdminUserAsync()
    {
        var options = seedOptions.Value;
        if (await userManager.FindByEmailAsync(options.AdminEmail) is not null) return;

        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            logger.LogWarning("Seed:AdminPassword is not configured; the default administrator was not created.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = options.AdminEmail,
            Email = options.AdminEmail,
            EmailConfirmed = true,
            FirstName = "System",
            LastName = "Administrator",
        };
        var result = await userManager.CreateAsync(admin, options.AdminPassword);
        if (result.Succeeded)
            result = await userManager.AddToRoleAsync(admin, SystemRoles.Administrator);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not seed admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        logger.LogInformation("Seeded administrator {Email}", options.AdminEmail);
    }
}
