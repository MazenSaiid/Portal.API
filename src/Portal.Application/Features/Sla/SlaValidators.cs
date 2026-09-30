using FluentValidation;
using Portal.Domain.Entities.Sla;

namespace Portal.Application.Features.Sla;

/// <summary>SL1 — sensible targets for every priority.</summary>
public sealed class SlaPoliciesValidator : AbstractValidator<IReadOnlyList<SlaPolicyDto>>
{
    private const int MaxMinutes = 60 * 24 * 60; // 60 days

    public SlaPoliciesValidator()
    {
        RuleFor(x => x).Must(list => list.Select(p => p.Priority).Distinct().Count() == Enum.GetValues<Domain.Entities.Tickets.TicketPriority>().Length
                                     && list.Count == Enum.GetValues<Domain.Entities.Tickets.TicketPriority>().Length)
            .WithName("Policies").WithMessage("Provide exactly one target per priority.");
        RuleForEach(x => x).ChildRules(p =>
        {
            p.RuleFor(x => x.FirstResponseMinutes).InclusiveBetween(1, MaxMinutes);
            p.RuleFor(x => x.ResolutionMinutes).InclusiveBetween(1, MaxMinutes)
                .GreaterThanOrEqualTo(x => x.FirstResponseMinutes).WithMessage("Resolution can't be faster than the first response.");
        });
    }
}

/// <summary>SL2.</summary>
public sealed class EscalationRuleRequestValidator : AbstractValidator<EscalationRuleRequest>
{
    public EscalationRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Trigger).IsInEnum();
        RuleFor(x => x.ThresholdMinutes).NotNull().InclusiveBetween(1, 60 * 24 * 60)
            .When(x => x.Trigger == SlaTrigger.UnassignedFor).WithMessage("Set how many minutes a ticket may stay unassigned.");
        RuleFor(x => x.MinPriority).IsInEnum().When(x => x.MinPriority is not null);
        RuleFor(x => x.RaisePriorityTo).IsInEnum().When(x => x.RaisePriorityTo is not null);
        RuleFor(x => x)
            .Must(x => x.Escalate || x.RaisePriorityTo is not null || x.NotifyAssignee || x.NotifySupervisors)
            .WithName("Actions").WithMessage("Choose at least one action.");
    }
}
