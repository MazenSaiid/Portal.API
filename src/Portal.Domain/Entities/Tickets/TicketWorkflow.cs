namespace Portal.Domain.Entities.Tickets;

/// <summary>The ticket status workflow (Spec 004, W1). The single definition used by services, API and UI.</summary>
public static class TicketWorkflow
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> Transitions = new Dictionary<TicketStatus, TicketStatus[]>
    {
        [TicketStatus.New] = [TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.Open] = [TicketStatus.InProgress, TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.InProgress] = [TicketStatus.Open, TicketStatus.OnHold, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.OnHold] = [TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed],
        [TicketStatus.Resolved] = [TicketStatus.Open, TicketStatus.Closed],
        [TicketStatus.Closed] = [TicketStatus.Open],
    };

    /// <summary>Statuses that still need work.</summary>
    public static readonly IReadOnlyList<TicketStatus> ActiveStatuses =
        [TicketStatus.New, TicketStatus.Open, TicketStatus.InProgress, TicketStatus.OnHold];

    public static IReadOnlyList<TicketStatus> AllowedFrom(TicketStatus current) => Transitions[current];

    public static bool CanMove(TicketStatus from, TicketStatus to) => Transitions[from].Contains(to);

    public static bool IsActive(TicketStatus status) => ActiveStatuses.Contains(status);
}
