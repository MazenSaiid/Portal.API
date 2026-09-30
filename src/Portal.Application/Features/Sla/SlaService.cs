using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities.Sla;

namespace Portal.Application.Features.Sla;

/// <summary>Admin configuration: SLA targets, auto-assignment and escalation rules.</summary>
public interface ISlaService
{
    Task<IReadOnlyList<SlaPolicyDto>> GetPoliciesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SlaPolicyDto>> UpdatePoliciesAsync(IReadOnlyList<SlaPolicyDto> policies, CancellationToken ct = default);
    Task<AutomationSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<AutomationSettingsDto> UpdateSettingsAsync(AutomationSettingsDto settings, CancellationToken ct = default);
    Task<IReadOnlyList<EscalationRuleDto>> GetRulesAsync(CancellationToken ct = default);
    Task<EscalationRuleDto> CreateRuleAsync(EscalationRuleRequest request, CancellationToken ct = default);
    Task<EscalationRuleDto> UpdateRuleAsync(int id, EscalationRuleRequest request, CancellationToken ct = default);
    Task DeleteRuleAsync(int id, CancellationToken ct = default);
}

public sealed class SlaService(
    IApplicationDbContext db,
    IValidator<IReadOnlyList<SlaPolicyDto>> policiesValidator,
    IValidator<EscalationRuleRequest> ruleValidator) : ISlaService
{
    public async Task<IReadOnlyList<SlaPolicyDto>> GetPoliciesAsync(CancellationToken ct = default) =>
        await db.SlaPolicies.AsNoTracking()
            .OrderByDescending(p => p.Priority)
            .Select(p => new SlaPolicyDto(p.Priority, p.FirstResponseMinutes, p.ResolutionMinutes))
            .ToListAsync(ct);

    /// <summary>New targets apply to new tickets and to tickets whose priority changes; existing promises are kept.</summary>
    public async Task<IReadOnlyList<SlaPolicyDto>> UpdatePoliciesAsync(IReadOnlyList<SlaPolicyDto> policies, CancellationToken ct = default)
    {
        await policiesValidator.ValidateAndThrowAsync(policies, ct);
        var existing = await db.SlaPolicies.ToDictionaryAsync(p => p.Priority, ct);
        foreach (var p in policies)
        {
            if (!existing.TryGetValue(p.Priority, out var policy))
                db.SlaPolicies.Add(policy = new SlaPolicy { Priority = p.Priority });
            policy.FirstResponseMinutes = p.FirstResponseMinutes;
            policy.ResolutionMinutes = p.ResolutionMinutes;
        }
        await db.SaveChangesAsync(ct);
        return await GetPoliciesAsync(ct);
    }

    public async Task<AutomationSettingsDto> GetSettingsAsync(CancellationToken ct = default) =>
        new(await db.AutomationSettings.AsNoTracking().Select(s => s.AutoAssignEnabled).FirstOrDefaultAsync(ct));

    public async Task<AutomationSettingsDto> UpdateSettingsAsync(AutomationSettingsDto settings, CancellationToken ct = default)
    {
        var row = await db.AutomationSettings.FirstOrDefaultAsync(ct);
        if (row is null) db.AutomationSettings.Add(row = new AutomationSettings { Id = 1 });
        row.AutoAssignEnabled = settings.AutoAssignEnabled;
        await db.SaveChangesAsync(ct);
        return new AutomationSettingsDto(row.AutoAssignEnabled);
    }

    public async Task<IReadOnlyList<EscalationRuleDto>> GetRulesAsync(CancellationToken ct = default) =>
        await db.EscalationRules.AsNoTracking()
            .OrderByDescending(r => r.IsActive).ThenBy(r => r.Name)
            .Select(r => new EscalationRuleDto(r.Id, r.Name, r.IsActive, r.Trigger, r.ThresholdMinutes, r.MinPriority, r.Escalate,
                r.RaisePriorityTo, r.NotifyAssignee, r.NotifySupervisors, db.EscalationRuleExecutions.Count(e => e.RuleId == r.Id)))
            .ToListAsync(ct);

    public async Task<EscalationRuleDto> CreateRuleAsync(EscalationRuleRequest request, CancellationToken ct = default)
    {
        await ruleValidator.ValidateAndThrowAsync(request, ct);
        var rule = new EscalationRule();
        Apply(rule, request);
        db.EscalationRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return (await GetRulesAsync(ct)).Single(r => r.Id == rule.Id);
    }

    public async Task<EscalationRuleDto> UpdateRuleAsync(int id, EscalationRuleRequest request, CancellationToken ct = default)
    {
        await ruleValidator.ValidateAndThrowAsync(request, ct);
        var rule = await db.EscalationRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Escalation rule", id);
        Apply(rule, request);
        await db.SaveChangesAsync(ct);
        return (await GetRulesAsync(ct)).Single(r => r.Id == id);
    }

    public async Task DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        var rule = await db.EscalationRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Escalation rule", id);
        db.EscalationRules.Remove(rule); // executions cascade
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(EscalationRule rule, EscalationRuleRequest r)
    {
        rule.Name = r.Name.Trim();
        rule.IsActive = r.IsActive;
        rule.Trigger = r.Trigger;
        rule.ThresholdMinutes = r.Trigger == SlaTrigger.UnassignedFor ? r.ThresholdMinutes : null;
        rule.MinPriority = r.MinPriority;
        rule.Escalate = r.Escalate;
        rule.RaisePriorityTo = r.RaisePriorityTo;
        rule.NotifyAssignee = r.NotifyAssignee;
        rule.NotifySupervisors = r.NotifySupervisors;
    }
}
