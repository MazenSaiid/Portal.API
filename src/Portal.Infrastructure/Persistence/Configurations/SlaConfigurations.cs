using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities.Sla;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> b)
    {
        b.ToTable("SlaPolicies");
        b.HasIndex(p => p.Priority).IsUnique();
    }
}

internal sealed class AutomationSettingsConfiguration : IEntityTypeConfiguration<AutomationSettings>
{
    public void Configure(EntityTypeBuilder<AutomationSettings> b)
    {
        b.ToTable("AutomationSettings");
        b.Property(s => s.Id).ValueGeneratedNever();
    }
}

internal sealed class EscalationRuleConfiguration : IEntityTypeConfiguration<EscalationRule>
{
    public void Configure(EntityTypeBuilder<EscalationRule> b)
    {
        b.ToTable("EscalationRules");
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(40);
    }
}

internal sealed class EscalationRuleExecutionConfiguration : IEntityTypeConfiguration<EscalationRuleExecution>
{
    public void Configure(EntityTypeBuilder<EscalationRuleExecution> b)
    {
        b.ToTable("EscalationRuleExecutions");
        b.HasKey(e => new { e.RuleId, e.TicketId });
        b.HasOne(e => e.Rule).WithMany().HasForeignKey(e => e.RuleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(e => e.Ticket).WithMany().HasForeignKey(e => e.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.Property(n => n.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(n => n.Title).HasMaxLength(200).IsRequired();
        b.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        b.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(n => new { n.UserId, n.IsRead, n.Id });
    }
}
