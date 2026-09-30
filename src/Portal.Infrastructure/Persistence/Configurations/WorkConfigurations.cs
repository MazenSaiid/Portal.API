using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities.Work;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> b)
    {
        b.ToTable("AgentTasks");
        b.Property(t => t.Title).HasMaxLength(200).IsRequired();
        b.Property(t => t.Notes).HasMaxLength(2000);

        b.HasOne(t => t.Owner).WithMany().HasForeignKey(t => t.OwnerId).OnDelete(DeleteBehavior.Cascade);
        // ClientSetNull (no DB action): SQL Server rejects a second cascade path from Users via Tickets.
        // TicketService/CustomerService unlink tasks before deleting (Spec 005, D6).
        b.HasOne(t => t.Ticket).WithMany().HasForeignKey(t => t.TicketId).OnDelete(DeleteBehavior.ClientSetNull);
        b.HasOne(t => t.Customer).WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.ClientSetNull);

        b.HasIndex(t => new { t.OwnerId, t.IsDone, t.DueAt });
    }
}

internal sealed class QuickReplyConfiguration : IEntityTypeConfiguration<QuickReply>
{
    public void Configure(EntityTypeBuilder<QuickReply> b)
    {
        b.ToTable("QuickReplies");
        b.Property(q => q.Title).HasMaxLength(100).IsRequired();
        b.Property(q => q.Body).HasMaxLength(4000).IsRequired();
        b.HasOne(q => q.Owner).WithMany().HasForeignKey(q => q.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(q => q.OwnerId);
    }
}
