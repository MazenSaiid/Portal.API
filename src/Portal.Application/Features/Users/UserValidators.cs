using FluentValidation;

namespace Portal.Application.Features.Users;

public abstract class UserProfileValidator<T> : AbstractValidator<T> where T : IUserProfileRequest
{
    protected UserProfileValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[0-9\s\-()]{6,20}$").WithMessage("Phone number is not valid.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.RoleId).NotEmpty().WithMessage("Role is required.");
    }
}

public sealed class CreateUserRequestValidator : UserProfileValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class UpdateUserRequestValidator : UserProfileValidator<UpdateUserRequest>;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8);
    }
}
