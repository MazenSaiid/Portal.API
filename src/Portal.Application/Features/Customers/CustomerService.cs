using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Application.Common.Models;
using Portal.Domain.Entities.Customers;

namespace Portal.Application.Features.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerListQuery query, CancellationToken ct = default);
    Task<CustomerDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(int id, CustomerRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<CustomerContactDto>> AddContactAsync(int customerId, ContactRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerContactDto>> UpdateContactAsync(int customerId, Guid contactId, ContactRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerContactDto>> DeleteContactAsync(int customerId, Guid contactId, CancellationToken ct = default);
}

public sealed partial class CustomerService(
    IApplicationDbContext db,
    IFileStorage storage,
    IValidator<CustomerRequest> customerValidator,
    IValidator<ContactRequest> contactValidator,
    ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerListQuery query, CancellationToken ct = default)
    {
        var customers = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var codeId = ParseCode(term);
            customers = customers.Where(c => c.Name.Contains(term)
                                             || (c.Email != null && c.Email.Contains(term))
                                             || (c.Phone != null && c.Phone.Contains(term))
                                             || (codeId != null && c.Id == codeId));
        }
        if (query.Type is { } type) customers = customers.Where(c => c.Type == type);
        if (query.IsActive is { } isActive) customers = customers.Where(c => c.IsActive == isActive);

        var total = await customers.CountAsync(ct);
        var items = await Sort(customers, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CustomerListItemDto(c.Id, c.Type, c.Name, c.Email, c.Phone, c.City, c.Country, c.IsActive,
                c.Interactions.Max(i => (DateTime?)i.OccurredAt), c.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<CustomerListItemDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<CustomerDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        await db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CustomerDto(
                c.Id, c.Type, c.Name, c.Email, c.Phone, c.PreferredChannel, c.PreferredLanguage,
                c.AddressLine, c.City, c.Country, c.IsActive,
                c.CreatedAt, db.Users.Where(u => u.Id == c.CreatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
                c.UpdatedAt, db.Users.Where(u => u.Id == c.UpdatedById).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
                c.Contacts.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name)
                    .Select(x => new CustomerContactDto(x.Id, x.Name, x.JobTitle, x.Email, x.Phone, x.IsPrimary)).ToList(),
                new CustomerStatsDto(c.Interactions.Count, c.Notes.Count, c.Attachments.Count,
                    c.Interactions.Max(i => (DateTime?)i.OccurredAt))))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Customer", id);

    public async Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct = default)
    {
        await customerValidator.ValidateAndThrowAsync(request, ct);
        var customer = new Customer();
        await ApplyAsync(customer, request, ct);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(customer.Id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(int id, CustomerRequest request, CancellationToken ct = default)
    {
        await customerValidator.ValidateAndThrowAsync(request, ct);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        await ApplyAsync(customer, request, ct);
        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer", id);
        var fileKeys = await db.CustomerAttachments.Where(a => a.CustomerId == id).Select(a => a.StorageKey).ToListAsync(ct);

        db.Customers.Remove(customer); // contacts, interactions, notes and attachment rows cascade (C8)
        await db.SaveChangesAsync(ct);

        // Files go after the database commit, so a failure never leaves rows pointing at missing files.
        foreach (var key in fileKeys)
        {
            try { await storage.DeleteAsync(key, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not delete stored file {Key} of customer {CustomerId}", key, id); }
        }
    }

    public async Task<IReadOnlyList<CustomerContactDto>> AddContactAsync(int customerId, ContactRequest request, CancellationToken ct = default)
    {
        await contactValidator.ValidateAndThrowAsync(request, ct);
        var customer = await LoadWithContactsAsync(customerId, ct);

        var contact = new CustomerContact { CustomerId = customerId };
        Apply(contact, request);
        customer.Contacts.Add(contact);
        EnsureSinglePrimary(customer, contact);

        await db.SaveChangesAsync(ct);
        return await GetContactsAsync(customerId, ct);
    }

    public async Task<IReadOnlyList<CustomerContactDto>> UpdateContactAsync(int customerId, Guid contactId, ContactRequest request, CancellationToken ct = default)
    {
        await contactValidator.ValidateAndThrowAsync(request, ct);
        var customer = await LoadWithContactsAsync(customerId, ct);
        var contact = customer.Contacts.FirstOrDefault(x => x.Id == contactId) ?? throw new NotFoundException("Contact", contactId);

        Apply(contact, request);
        EnsureSinglePrimary(customer, contact);

        await db.SaveChangesAsync(ct);
        return await GetContactsAsync(customerId, ct);
    }

    public async Task<IReadOnlyList<CustomerContactDto>> DeleteContactAsync(int customerId, Guid contactId, CancellationToken ct = default)
    {
        var customer = await LoadWithContactsAsync(customerId, ct);
        var contact = customer.Contacts.FirstOrDefault(x => x.Id == contactId) ?? throw new NotFoundException("Contact", contactId);

        db.CustomerContacts.Remove(contact);
        await db.SaveChangesAsync(ct);
        return await GetContactsAsync(customerId, ct);
    }

    private async Task ApplyAsync(Customer customer, CustomerRequest request, CancellationToken ct)
    {
        var email = NullIfBlank(request.Email);
        var normalized = email?.ToUpperInvariant();

        // CR3 / C4
        if (normalized is not null &&
            await db.Customers.AnyAsync(c => c.NormalizedEmail == normalized && c.Id != customer.Id, ct))
            throw new ConflictException($"A customer with email '{email}' already exists.");

        customer.Type = request.Type;
        customer.Name = request.Name.Trim();
        customer.Email = email;
        customer.NormalizedEmail = normalized;
        customer.Phone = NullIfBlank(request.Phone);
        customer.PreferredChannel = request.PreferredChannel;
        customer.PreferredLanguage = request.PreferredLanguage;
        customer.AddressLine = NullIfBlank(request.AddressLine);
        customer.City = NullIfBlank(request.City);
        customer.Country = NullIfBlank(request.Country);
        customer.IsActive = request.IsActive;
    }

    private static void Apply(CustomerContact contact, ContactRequest request)
    {
        contact.Name = request.Name.Trim();
        contact.JobTitle = NullIfBlank(request.JobTitle);
        contact.Email = NullIfBlank(request.Email);
        contact.Phone = NullIfBlank(request.Phone);
        contact.IsPrimary = request.IsPrimary;
    }

    /// <summary>CR4 — at most one primary contact per customer.</summary>
    private static void EnsureSinglePrimary(Customer customer, CustomerContact changed)
    {
        if (!changed.IsPrimary) return;
        foreach (var other in customer.Contacts.Where(x => x != changed && x.IsPrimary))
            other.IsPrimary = false;
    }

    private async Task<Customer> LoadWithContactsAsync(int id, CancellationToken ct) =>
        await db.Customers.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Customer", id);

    private async Task<IReadOnlyList<CustomerContactDto>> GetContactsAsync(int customerId, CancellationToken ct) =>
        await db.CustomerContacts.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name)
            .Select(x => new CustomerContactDto(x.Id, x.Name, x.JobTitle, x.Email, x.Phone, x.IsPrimary))
            .ToListAsync(ct);

    private static IQueryable<Customer> Sort(IQueryable<Customer> customers, CustomerListQuery q) =>
        (q.SortBy?.ToLowerInvariant(), q.IsDescending) switch
        {
            ("name", false) => customers.OrderBy(c => c.Name),
            ("name", true) => customers.OrderByDescending(c => c.Name),
            ("code", false) => customers.OrderBy(c => c.Id),
            ("code", true) => customers.OrderByDescending(c => c.Id),
            ("city", false) => customers.OrderBy(c => c.City),
            ("city", true) => customers.OrderByDescending(c => c.City),
            ("status", false) => customers.OrderBy(c => c.IsActive),
            ("status", true) => customers.OrderByDescending(c => c.IsActive),
            ("lastinteractionat", false) => customers.OrderBy(c => c.Interactions.Max(i => (DateTime?)i.OccurredAt)),
            ("lastinteractionat", true) => customers.OrderByDescending(c => c.Interactions.Max(i => (DateTime?)i.OccurredAt)),
            ("createdat", false) => customers.OrderBy(c => c.CreatedAt),
            _ => customers.OrderByDescending(c => c.CreatedAt),
        };

    /// <summary>"CUS-00042", "cus42" or "42" → 42.</summary>
    private static int? ParseCode(string term)
    {
        var match = CodePattern().Match(term);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    [GeneratedRegex(@"^(?:CUS-?)?0*(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex CodePattern();

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
