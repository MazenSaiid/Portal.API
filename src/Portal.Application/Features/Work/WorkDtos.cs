using Portal.Application.Features.Sla;
using Portal.Application.Features.Tickets;
using Portal.Domain.Entities.Customers;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Work;

// ---------- Tasks ----------

public sealed class TaskQuery
{
    /// <summary><c>open</c> (default), <c>done</c> or <c>all</c>.</summary>
    public string? Status { get; set; }
}

public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Notes,
    DateTime? DueAt,
    bool IsDone,
    DateTime? CompletedAt,
    int? TicketId,
    string? TicketSubject,
    int? CustomerId,
    string? CustomerName,
    DateTime CreatedAt)
{
    public string? TicketCode => TicketId is { } id ? Ticket.FormatCode(id) : null;
    public string? CustomerCode => CustomerId is { } id ? Customer.FormatCode(id) : null;
    public bool IsOverdue => !IsDone && DueAt < DateTime.UtcNow;
}

public sealed record TaskRequest(string Title, string? Notes, DateTime? DueAt, int? TicketId, int? CustomerId);

public sealed record CompleteTaskRequest(bool IsDone);

public sealed record RemindersDto(int Overdue, int DueToday)
{
    public int Total => Overdue + DueToday;
}

// ---------- Quick replies ----------

public sealed record QuickReplyDto(int Id, string Title, string Body, bool IsShared, bool CanEdit);

public sealed record QuickReplyRequest(string Title, string Body, bool IsShared);

// ---------- Dashboard ----------

public sealed record DashboardSummaryDto(
    int MyActive,
    int InProgress,
    int Escalated,
    int HighPriority,
    int Unassigned,
    int ResolvedLast7Days);

public sealed record DashboardCustomerDto(int Id, string Name, string? Email, string? Phone, int OtherActiveTickets)
{
    public string Code => Customer.FormatCode(Id);
}

public sealed record DashboardTicketDto(
    int Id,
    string Subject,
    TicketPriority Priority,
    TicketStatus Status,
    bool IsEscalated,
    string CategoryName,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    DashboardCustomerDto Customer,
    TicketSlaDto Sla)
{
    public string Code => Ticket.FormatCode(Id);
}

public sealed record TeamActivityDto(
    long Id,
    int TicketId,
    string TicketSubject,
    TicketEventType Type,
    string? FromValue,
    string? ToValue,
    string? Message,
    DateTime CreatedAt,
    string? CreatedByName)
{
    public string TicketCode => Ticket.FormatCode(TicketId);
}

public sealed record AgentDashboardDto(
    bool CanViewTickets,
    DashboardSummaryDto Summary,
    IReadOnlyList<DashboardTicketDto> MyTickets,
    IReadOnlyList<DashboardTicketDto> UnassignedQueue,
    IReadOnlyList<AssigneeDto> Team,
    IReadOnlyList<TeamActivityDto> TeamActivity,
    IReadOnlyList<TaskDto> Tasks,
    RemindersDto Reminders);
