using FluentValidation;

namespace Portal.Application.Features.Work;

public sealed class TaskRequestValidator : AbstractValidator<TaskRequest>
{
    public TaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.TicketId).GreaterThan(0).When(x => x.TicketId is not null);
        RuleFor(x => x.CustomerId).GreaterThan(0).When(x => x.CustomerId is not null);
    }
}

public sealed class QuickReplyRequestValidator : AbstractValidator<QuickReplyRequest>
{
    public QuickReplyRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}
