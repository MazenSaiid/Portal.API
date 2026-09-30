namespace Portal.Domain.Entities.Sla;

public enum SlaState { None, OnTrack, AtRisk, Breached, Met }

/// <summary>Pure SLA arithmetic (Spec 007, S1, S4, S5). Shared by ticket services, DTOs and the rule engine.</summary>
public static class SlaCalculator
{
    /// <summary>Share of the time window after which a target counts as "at risk".</summary>
    public const double AtRiskShare = 0.75;

    public sealed record Dates(DateTime FirstResponseDueAt, DateTime ResolutionDueAt, DateTime ResolutionAtRiskAt);

    public static Dates For(DateTime createdAt, SlaPolicy policy) => new(
        createdAt.AddMinutes(policy.FirstResponseMinutes),
        createdAt.AddMinutes(policy.ResolutionMinutes),
        createdAt.AddMinutes(policy.ResolutionMinutes * AtRiskShare));

    /// <param name="completedAt">When the target was met (first response / resolution), or null if still open.</param>
    public static SlaState State(DateTime startedAt, DateTime? dueAt, DateTime? completedAt, DateTime now)
    {
        if (dueAt is not { } due) return SlaState.None;
        if (completedAt is { } done) return done <= due ? SlaState.Met : SlaState.Breached;
        if (now > due) return SlaState.Breached;
        var used = (now - startedAt).TotalMinutes;
        var total = (due - startedAt).TotalMinutes;
        return total > 0 && used >= total * AtRiskShare ? SlaState.AtRisk : SlaState.OnTrack;
    }
}
