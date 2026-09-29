using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities.Tickets;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> b)
    {
        b.ToTable("Tickets");
        b.Property(t => t.Subject).HasMaxLength(200).IsRequired();
        b.Property(t => t.Description).HasMaxLength(8000).IsRequired();
        // Priority and status stay numeric so sorting follows severity / workflow order, not the alphabet.
        b.Property(t => t.Channel).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.EscalationReason).HasMaxLength(1000);

        // K7 - a customer with tickets can't be deleted; categories in use can't be deleted (T3).
        b.HasOne(t => t.Customer).WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
        // Deleting a user leaves their tickets unassigned rather than deleting them.
        b.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(t => t.History).WithOne(h => h.Ticket).HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(t => new { t.Status, t.Priority });
        b.HasIndex(t => t.AssigneeId);
        b.HasIndex(t => t.CustomerId);
        b.HasIndex(t => t.LastActivityAt);
    }
}

internal sealed class TicketCategoryConfiguration : IEntityTypeConfiguration<TicketCategory>
{
    public void Configure(EntityTypeBuilder<TicketCategory> b)
    {
        b.ToTable("TicketCategories");
        b.Property(c => c.Name).HasMaxLength(80).IsRequired();
        b.Property(c => c.Description).HasMaxLength(300);
        b.HasIndex(c => c.Name).IsUnique();
    }
}

internal sealed class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistoryEntry>
{
    public void Configure(EntityTypeBuilder<TicketHistoryEntry> b)
    {
        b.ToTable("TicketHistory");
        b.Property(h => h.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(h => h.FromValue).HasMaxLength(200);
        b.Property(h => h.ToValue).HasMaxLength(200);
        b.Property(h => h.Message).HasMaxLength(4000);
        b.HasIndex(h => h.TicketId);
    }
}
