using Portal.Application.Common.Models;
using Portal.Application.Features.Sla;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Tickets;

public sealed class TicketListQuery : PagedQuery
{
    public string? Search { get; set; }

    /// <summary>Repeat to match several: <c>?status=New&amp;status=Open</c>.</summary>
    public List<TicketStatus>? Status { get; set; }

    public TicketPriority? Priority { get; set; }
    public int? CategoryId { get; set; }

    /// <summary><c>me</c>, <c>unassigned</c> or a user id.</summary>
    public string? AssignedTo { get; set; }

    public int? CustomerId { get; set; }
    public bool? Escalated { get; set; }

    /// <summary><c>breached</c> or <c>atRisk</c> (Spec 007).</summary>
    public string? Sla { get; set; }
}

public sealed record TicketListItemDto(
    int Id,
    string Subject,
    int CustomerId,
    string CustomerName,
    string CategoryName,
    TicketPriority Priority,
    TicketStatus Status,
    Guid? AssigneeId,
    string? AssigneeName,
    bool IsEscalated,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    TicketSlaDto Sla)
{
    public string Code => Ticket.FormatCode(Id);
    public string CustomerCode => Customer.FormatCode(CustomerId);
}

public sealed record TicketCustomerDto(int Id, string Name, string? Email, string? Phone, bool IsActive)
{
    public string Code => Customer.FormatCode(Id);
}

public sealed record TicketDto(
    int Id,
    string Subject,
    string Description,
    TicketCustomerDto Customer,
    int CategoryId,
    string CategoryName,
    TicketPriority Priority,
    TicketStatus Status,
    TicketChannel Channel,
    Guid? AssigneeId,
    string? AssigneeName,
    bool IsEscalated,
    DateTime? EscalatedAt,
    string? EscalationReason,
    DateTime? ResolvedAt,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    string? CreatedByName,
    DateTime LastActivityAt,
    TicketSlaDto Sla)
{
    public string Code => Ticket.FormatCode(Id);

    /// <summary>Statuses the ticket may move to next (W1), so the UI only offers valid moves.</summary>
    public IReadOnlyList<TicketStatus> AllowedStatuses => TicketWorkflow.AllowedFrom(Status);
}

public sealed record CreateTicketRequest(
    int CustomerId,
    string Subject,
    string Description,
    int CategoryId,
    TicketPriority Priority,
    TicketChannel Channel,
    Guid? AssigneeId = null);

public sealed record UpdateTicketRequest(
    string Subject,
    string Description,
    int CategoryId,
    TicketPriority Priority,
    TicketChannel Channel);

public sealed record ChangeStatusRequest(TicketStatus Status, string? Comment);

public sealed record AssignTicketRequest(Guid? AssigneeId);

public sealed record EscalateTicketRequest(string Reason);

public sealed record DeEscalateTicketRequest(string? Comment);

public sealed record TicketCommentRequest(string Content);

public sealed record TicketHistoryDto(
    long Id,
    TicketEventType Type,
    string? FromValue,
    string? ToValue,
    string? Message,
    DateTime CreatedAt,
    string? CreatedByName);

public sealed record AssigneeDto(Guid Id, string FullName, string Email, int ActiveTickets);

// ---------- Categories ----------

public sealed record TicketCategoryDto(int Id, string Name, string? Description, bool IsActive, int TicketCount);

public sealed record TicketCategoryRequest(string Name, string? Description, bool IsActive = true);
