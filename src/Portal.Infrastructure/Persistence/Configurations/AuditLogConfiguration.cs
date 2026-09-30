using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities.Auditing;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(l => l.UserName).HasMaxLength(256);
        b.Property(l => l.IpAddress).HasMaxLength(64);
        b.Property(l => l.Action).HasConversion<string>().HasMaxLength(30);
        b.Property(l => l.EntityType).HasMaxLength(50).IsRequired();
        b.Property(l => l.EntityId).HasMaxLength(100);
        b.Property(l => l.Summary).HasMaxLength(500).IsRequired();

        // No FK to Users: entries must outlive the users they mention.
        b.HasIndex(l => l.OccurredAt);
        b.HasIndex(l => l.UserId);
        b.HasIndex(l => new { l.EntityType, l.EntityId });
    }
}
