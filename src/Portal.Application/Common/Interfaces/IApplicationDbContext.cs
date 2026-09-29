using Microsoft.EntityFrameworkCore;
using Portal.Domain.Entities;

namespace Portal.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<ApplicationRole> Roles { get; }
    DbSet<ApplicationUserRole> UserRoles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
