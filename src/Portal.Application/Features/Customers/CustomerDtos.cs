using Portal.Application.Common.Models;
using Portal.Domain.Entities.Customers;

namespace Portal.Application.Features.Customers;

public sealed class CustomerListQuery : PagedQuery
{
    public string? Search { get; set; }
    public CustomerType? Type { get; set; }
    public bool? IsActive { get; set; }
}

public sealed record CustomerListItemDto(
    int Id,
    CustomerType Type,
    string Name,
    string? Email,
    string? Phone,
    string? City,
    string? Country,
    bool IsActive,
    DateTime? LastInteractionAt,
    DateTime CreatedAt)
{
    public string Code => Customer.FormatCode(Id);
}

public sealed record CustomerContactDto(Guid Id, string Name, string? JobTitle, string? Email, string? Phone, bool IsPrimary);

public sealed record CustomerStatsDto(int Interactions, int Notes, int Attachments, DateTime? LastInteractionAt);

public sealed record CustomerDto(
    int Id,
    CustomerType Type,
    string Name,
    string? Email,
    string? Phone,
    ContactChannel PreferredChannel,
    PreferredLanguage PreferredLanguage,
    string? AddressLine,
    string? City,
    string? Country,
    bool IsActive,
    DateTime CreatedAt,
    string? CreatedByName,
    DateTime? UpdatedAt,
    string? UpdatedByName,
    IReadOnlyList<CustomerContactDto> Contacts,
    CustomerStatsDto Stats)
{
    public string Code => Customer.FormatCode(Id);
}

/// <summary>Used for both create and update.</summary>
public sealed record CustomerRequest(
    CustomerType Type,
    string Name,
    string? Email,
    string? Phone,
    ContactChannel PreferredChannel,
    PreferredLanguage PreferredLanguage,
    string? AddressLine,
    string? City,
    string? Country,
    bool IsActive = true);

public sealed record ContactRequest(string Name, string? JobTitle, string? Email, string? Phone, bool IsPrimary);

// ---------- Activity ----------

public sealed class InteractionQuery : PagedQuery;

public sealed record InteractionDto(
    Guid Id,
    InteractionType Type,
    InteractionDirection Direction,
    string Subject,
    string? Summary,
    DateTime OccurredAt,
    DateTime CreatedAt,
    string? CreatedByName);

/// <summary>`OccurredAt` defaults to now when omitted.</summary>
public sealed record LogInteractionRequest(
    InteractionType Type,
    InteractionDirection Direction,
    string Subject,
    string? Summary,
    DateTime? OccurredAt);

/// <summary>`CanManage` tells the UI whether the current user may edit/delete it (CR7).</summary>
public sealed record NoteDto(
    Guid Id,
    string Content,
    DateTime CreatedAt,
    string? CreatedByName,
    DateTime? UpdatedAt,
    bool CanManage);

public sealed record NoteRequest(string Content);

public sealed record AttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAt,
    string? CreatedByName,
    bool CanManage);

/// <summary>An uploaded file as the Application layer sees it, independent of ASP.NET's IFormFile.</summary>
public sealed record UploadedFile(Stream Content, string FileName, long Length);

public sealed record FileDownload(Stream Content, string FileName, string ContentType);
