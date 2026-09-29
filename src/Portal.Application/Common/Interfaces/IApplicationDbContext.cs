using Microsoft.EntityFrameworkCore;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Customers;

namespace Portal.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<ApplicationRole> Roles { get; }
    DbSet<ApplicationUserRole> UserRoles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerContact> CustomerContacts { get; }
    DbSet<CustomerInteraction> CustomerInteractions { get; }
    DbSet<CustomerNote> CustomerNotes { get; }
    DbSet<CustomerAttachment> CustomerAttachments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
