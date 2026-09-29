using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Portal.Domain.Entities.Customers;

namespace Portal.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.PreferredChannel).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.PreferredLanguage).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Property(c => c.Email).HasMaxLength(256);
        b.Property(c => c.NormalizedEmail).HasMaxLength(256);
        b.Property(c => c.Phone).HasMaxLength(20);
        b.Property(c => c.AddressLine).HasMaxLength(300);
        b.Property(c => c.City).HasMaxLength(100);
        b.Property(c => c.Country).HasMaxLength(100);

        b.HasIndex(c => c.NormalizedEmail).IsUnique().HasFilter("NormalizedEmail IS NOT NULL");
        b.HasIndex(c => c.Name);
        b.HasIndex(c => c.Phone);

        b.HasMany(c => c.Contacts).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Interactions).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Notes).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Attachments).WithOne(x => x.Customer).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> b)
    {
        b.ToTable("CustomerContacts");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.JobTitle).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(20);
    }
}

internal sealed class CustomerInteractionConfiguration : IEntityTypeConfiguration<CustomerInteraction>
{
    public void Configure(EntityTypeBuilder<CustomerInteraction> b)
    {
        b.ToTable("CustomerInteractions");
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(4000);
        b.HasIndex(x => new { x.CustomerId, x.OccurredAt });
    }
}

internal sealed class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> b)
    {
        b.ToTable("CustomerNotes");
        b.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.CustomerId, x.CreatedAt });
    }
}

internal sealed class CustomerAttachmentConfiguration : IEntityTypeConfiguration<CustomerAttachment>
{
    public void Configure(EntityTypeBuilder<CustomerAttachment> b)
    {
        b.ToTable("CustomerAttachments");
        b.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.CustomerId);
    }
}
