using FluentValidation;
using Portal.Domain.Entities.Customers;

namespace Portal.Application.Features.Customers;

internal static class ContactRules
{
    public const string PhonePattern = @"^\+?[0-9\s\-()]{6,20}$";
}

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(ContactRules.PhonePattern).WithMessage("Phone number is not valid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.PreferredChannel).IsInEnum();
        RuleFor(x => x.PreferredLanguage).IsInEnum();
        RuleFor(x => x.AddressLine).MaximumLength(300);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);

        // CR1 — the customer must be reachable somehow.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Enter an email or a phone number.")
            .When(x => string.IsNullOrWhiteSpace(x.Phone));

        // CR2 — the preferred channel must be one we can actually use.
        RuleFor(x => x.PreferredChannel)
            .Must((request, channel) => channel == ContactChannel.Email
                ? !string.IsNullOrWhiteSpace(request.Email)
                : !string.IsNullOrWhiteSpace(request.Phone))
            .WithMessage(x => x.PreferredChannel == ContactChannel.Email
                ? "Email is the preferred channel, so an email is required."
                : $"{x.PreferredChannel} is the preferred channel, so a phone number is required.");
    }
}

public sealed class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    public ContactRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.JobTitle).MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(ContactRules.PhonePattern).WithMessage("Phone number is not valid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        // CR5
        RuleFor(x => x.Email).NotEmpty().WithMessage("Enter an email or a phone number.")
            .When(x => string.IsNullOrWhiteSpace(x.Phone));
    }
}

public sealed class LogInteractionRequestValidator : AbstractValidator<LogInteractionRequest>
{
    public LogInteractionRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Summary).MaximumLength(4000);
        // CR6 — small tolerance for clock differences between browser and server.
        RuleFor(x => x.OccurredAt)
            .Must(at => at is null || at.Value.ToUniversalTime() <= clock.GetUtcNow().UtcDateTime.AddMinutes(5))
            .WithMessage("The interaction cannot be in the future.");
    }
}

public sealed class NoteRequestValidator : AbstractValidator<NoteRequest>
{
    public NoteRequestValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
    }
}
