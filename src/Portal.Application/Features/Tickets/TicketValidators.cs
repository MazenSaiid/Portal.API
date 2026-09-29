using FluentValidation;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Tickets;

public sealed class CreateTicketRequestValidator : AbstractValidator<CreateTicketRequest>
{
    public CreateTicketRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0).WithMessage("Choose a customer.");
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Choose a category.");
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();
    }
}

public sealed class UpdateTicketRequestValidator : AbstractValidator<UpdateTicketRequest>
{
    public UpdateTicketRequestValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Choose a category.");
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();
    }
}

public sealed class ChangeStatusRequestValidator : AbstractValidator<ChangeStatusRequest>
{
    public ChangeStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(4000);
        // W3
        RuleFor(x => x.Comment).NotEmpty().WithMessage("Describe how the issue was resolved.")
            .When(x => x.Status == TicketStatus.Resolved);
    }
}

public sealed class EscalateTicketRequestValidator : AbstractValidator<EscalateTicketRequest>
{
    public EscalateTicketRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Give a reason for the escalation.").MaximumLength(1000);
    }
}

public sealed class DeEscalateTicketRequestValidator : AbstractValidator<DeEscalateTicketRequest>
{
    public DeEscalateTicketRequestValidator()
    {
        RuleFor(x => x.Comment).MaximumLength(4000);
    }
}

public sealed class TicketCommentRequestValidator : AbstractValidator<TicketCommentRequest>
{
    public TicketCommentRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}

public sealed class TicketCategoryRequestValidator : AbstractValidator<TicketCategoryRequest>
{
    public TicketCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Description).MaximumLength(300);
    }
}
