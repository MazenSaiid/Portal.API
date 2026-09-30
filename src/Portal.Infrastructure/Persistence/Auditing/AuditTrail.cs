using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities;
using Portal.Domain.Entities.Auditing;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;
using Portal.Domain.Entities.Work;

namespace Portal.Infrastructure.Persistence.Auditing;

/// <summary>
/// Turns change-tracker entries into audit log rows (Spec 006, L1–L4). <see cref="Capture"/> runs before the save,
/// <see cref="BuildAsync"/> after it, when generated ids are known.
/// </summary>
internal static class AuditTrail
{
    /// <summary>
    /// Key and summary of updated/deleted rows are taken before the save (related names still exist);
    /// for created rows they are filled in afterwards, when generated ids are known.
    /// </summary>
    public sealed class Pending(EntityEntry entry, AuditAction action, List<AuditChange> changes, string? key, string? summary)
    {
        public EntityEntry Entry { get; } = entry;
        public AuditAction Action { get; } = action;
        public List<AuditChange> Changes { get; } = changes;
        public string? Key { get; } = key;
        public string? Summary { get; } = summary;
    }

    /// <summary>L2 — audited types and the name they are logged under.</summary>
    private static readonly Dictionary<Type, string> Audited = new()
    {
        [typeof(ApplicationUser)] = "User",
        [typeof(ApplicationRole)] = "Role",
        [typeof(ApplicationUserRole)] = "UserRole",
        [typeof(RolePermission)] = "RolePermission",
        [typeof(Customer)] = "Customer",
        [typeof(CustomerContact)] = "CustomerContact",
        [typeof(CustomerInteraction)] = "CustomerInteraction",
        [typeof(CustomerNote)] = "CustomerNote",
        [typeof(CustomerAttachment)] = "CustomerAttachment",
        [typeof(Ticket)] = "Ticket",
        [typeof(TicketCategory)] = "TicketCategory",
        [typeof(QuickReply)] = "QuickReply",
        [typeof(SlaPolicy)] = "SlaPolicy",
        [typeof(AutomationSettings)] = "AutomationSettings",
        [typeof(EscalationRule)] = "EscalationRule",
    };

    /// <summary>L4 / AL5 — secrets, derived copies and noise fields are never logged.</summary>
    private static readonly HashSet<string> Excluded =
    [
        nameof(ApplicationUser.PasswordHash), nameof(ApplicationUser.SecurityStamp), nameof(ApplicationUser.ConcurrencyStamp),
        nameof(ApplicationUser.NormalizedEmail), nameof(ApplicationUser.NormalizedUserName), nameof(ApplicationRole.NormalizedName),
        nameof(ApplicationUser.LastLoginAt), nameof(ApplicationUser.AccessFailedCount), nameof(ApplicationUser.LockoutEnd),
        nameof(CustomerAttachment.StorageKey), nameof(Customer.NormalizedEmail), nameof(Ticket.LastActivityAt),
        "CreatedAt", "CreatedById", "UpdatedAt", "UpdatedById",
    ];

    private const int MaxValueLength = 500;

    public static async Task<List<Pending>> CaptureAsync(AppDbContext db, CancellationToken ct)
    {
        var tracker = db.ChangeTracker;
        var pending = new List<Pending>();
        foreach (var entry in tracker.Entries())
        {
            if (!Audited.ContainsKey(entry.Metadata.ClrType)) continue;

            var action = entry.State switch
            {
                EntityState.Added => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                EntityState.Deleted => AuditAction.Deleted,
                _ => (AuditAction?)null,
            };
            if (action is null) continue;

            var changes = new List<AuditChange>();
            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (Excluded.Contains(name) || property.Metadata.IsShadowProperty()) continue;

                switch (action)
                {
                    case AuditAction.Created when property.CurrentValue is not null && !property.Metadata.IsPrimaryKey():
                        changes.Add(new AuditChange(name, null, Format(property.CurrentValue)));
                        break;
                    case AuditAction.Deleted when property.OriginalValue is not null && !property.Metadata.IsPrimaryKey():
                        changes.Add(new AuditChange(name, Format(property.OriginalValue), null));
                        break;
                    case AuditAction.Updated when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                        changes.Add(new AuditChange(name, Format(property.OriginalValue), Format(property.CurrentValue)));
                        break;
                }
            }

            // AL5 — an update that only touched excluded fields isn't worth an entry.
            if (action == AuditAction.Updated && changes.Count == 0) continue;
            var created = action == AuditAction.Created;
            pending.Add(new Pending(entry, action.Value, changes,
                created ? null : KeyOf(entry),
                created ? null : await DescribeAsync(db, entry.Entity, action.Value, ct)));
        }
        return pending;
    }

    public static async Task<List<AuditLog>> BuildAsync(AppDbContext db, IEnumerable<Pending> pending, ICurrentUser user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var logs = new List<AuditLog>();
        foreach (var p in pending)
        {
            logs.Add(new AuditLog
            {
                OccurredAt = now,
                UserId = user.UserId,
                UserName = user.UserName ?? (user.UserId is null ? "System" : null),
                IpAddress = user.IpAddress,
                Action = p.Action,
                EntityType = Audited[p.Entry.Metadata.ClrType],
                EntityId = p.Key ?? KeyOf(p.Entry),
                Summary = p.Summary ?? await DescribeAsync(db, p.Entry.Entity, p.Action, ct),
                Changes = p.Changes.Count == 0 ? null : JsonSerializer.Serialize(p.Changes),
            });
        }
        return logs;
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join(":", entry.Metadata.FindPrimaryKey()!.Properties.Select(k => entry.Property(k.Name).CurrentValue));

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC",
        DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC",
        bool b => b ? "Yes" : "No",
        _ => Truncate(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private static string? Truncate(string? s) => s is { Length: > MaxValueLength } ? s[..MaxValueLength] + "…" : s;

    private static string Verb(AuditAction action) => action switch
    {
        AuditAction.Created => "created",
        AuditAction.Deleted => "deleted",
        _ => "updated",
    };

    /// <summary>A sentence an administrator can read without looking up ids.</summary>
    private static async Task<string> DescribeAsync(AppDbContext db, object entity, AuditAction action, CancellationToken ct)
    {
        var verb = Verb(action);
        switch (entity)
        {
            case ApplicationUser u: return $"User {u.Email} {verb}";
            case ApplicationRole r: return $"Role {r.Name} {verb}";
            case Customer c: return $"Customer {c.Name} ({Customer.FormatCode(c.Id)}) {verb}";
            case CustomerContact x: return $"Contact {x.Name} of {Customer.FormatCode(x.CustomerId)} {verb}";
            case CustomerInteraction x: return $"Interaction \"{x.Subject}\" on {Customer.FormatCode(x.CustomerId)} {verb}";
            case CustomerNote x: return $"Note on {Customer.FormatCode(x.CustomerId)} {verb}";
            case CustomerAttachment x: return $"File {x.FileName} on {Customer.FormatCode(x.CustomerId)} {verb}";
            case Ticket t: return $"Ticket {Ticket.FormatCode(t.Id)} \"{t.Subject}\" {verb}";
            case TicketCategory c: return $"Ticket category {c.Name} {verb}";
            case SlaPolicy sp: return $"SLA targets for {sp.Priority} priority {verb}";
            case AutomationSettings: return $"Automation settings {verb}";
            case EscalationRule er: return $"Escalation rule \"{er.Name}\" {verb}";
            case QuickReply q: return $"{(q.OwnerId is null ? "Shared" : "Personal")} quick reply \"{q.Title}\" {verb}";
            case RolePermission rp:
            {
                var role = await db.Roles.AsNoTracking().Where(r => r.Id == rp.RoleId).Select(r => r.Name).FirstOrDefaultAsync(ct);
                var key = await db.Permissions.AsNoTracking().Where(p => p.Id == rp.PermissionId).Select(p => p.Key).FirstOrDefaultAsync(ct);
                return action == AuditAction.Deleted
                    ? $"Permission {key} revoked from role {role}"
                    : $"Permission {key} granted to role {role}";
            }
            case ApplicationUserRole ur:
            {
                var email = await db.Users.AsNoTracking().Where(u => u.Id == ur.UserId).Select(u => u.Email).FirstOrDefaultAsync(ct);
                var role = await db.Roles.AsNoTracking().Where(r => r.Id == ur.RoleId).Select(r => r.Name).FirstOrDefaultAsync(ct);
                return action == AuditAction.Deleted
                    ? $"User {email ?? ur.UserId.ToString()} removed from role {role ?? ur.RoleId.ToString()}"
                    : $"User {email ?? ur.UserId.ToString()} assigned to role {role ?? ur.RoleId.ToString()}";
            }
            default: return $"{entity.GetType().Name} {verb}";
        }
    }
}
