using Portal.Domain.Common;

namespace Portal.Domain.Entities.Customers;

public class Customer : AuditableEntity
{
    public int Id { get; set; }
    public CustomerType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>Upper-cased email used for the case-insensitive uniqueness check.</summary>
    public string? NormalizedEmail { get; set; }

    public string? Phone { get; set; }
    public ContactChannel PreferredChannel { get; set; }
    public PreferredLanguage PreferredLanguage { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();
    public ICollection<CustomerInteraction> Interactions { get; set; } = new List<CustomerInteraction>();
    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();
    public ICollection<CustomerAttachment> Attachments { get; set; } = new List<CustomerAttachment>();

    /// <summary>Human-friendly code, e.g. CUS-00042.</summary>
    public static string FormatCode(int id) => $"CUS-{id:D5}";
}
