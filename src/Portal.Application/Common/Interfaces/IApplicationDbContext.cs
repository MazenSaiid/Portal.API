using Microsoft.EntityFrameworkCore;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Auditing;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;
using Portal.Domain.Entities.Work;

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
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketCategory> TicketCategories { get; }
    DbSet<TicketHistoryEntry> TicketHistory { get; }
    DbSet<AgentTask> AgentTasks { get; }
    DbSet<QuickReply> QuickReplies { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SlaPolicy> SlaPolicies { get; }
    DbSet<AutomationSettings> AutomationSettings { get; }
    DbSet<EscalationRule> EscalationRules { get; }
    DbSet<EscalationRuleExecution> EscalationRuleExecutions { get; }
    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
