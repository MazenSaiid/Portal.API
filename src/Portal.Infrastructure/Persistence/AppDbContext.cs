using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Common;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Tickets;
using Portal.Domain.Entities.Work;

namespace Portal.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid, IdentityUserClaim<Guid>, ApplicationUserRole,
        IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>(options), IApplicationDbContext
{
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<CustomerInteraction> CustomerInteractions => Set<CustomerInteraction>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<CustomerAttachment> CustomerAttachments => Set<CustomerAttachment>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();
    public DbSet<TicketHistoryEntry> TicketHistory => Set<TicketHistoryEntry>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<QuickReply> QuickReplies => Set<QuickReply>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Sets Created/Updated At/By on every auditable entity being saved (Spec 003, C9).</summary>
    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        var userId = currentUser.UserId;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedById = userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedById = userId;
                entry.Property(e => e.CreatedAt).IsModified = false;
                entry.Property(e => e.CreatedById).IsModified = false;
            }
        }
    }

    /// <summary>All timestamps are stored in UTC; mark them as such when read so JSON carries the 'Z'.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Shorter, readable Identity table names.
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<ApplicationUserRole>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }
}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
