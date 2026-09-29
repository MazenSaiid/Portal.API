using FluentValidation;

namespace Portal.Application.Features.Roles;

public sealed class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64)
            .Matches(@"^[\p{L}\p{N} _\-]+$").WithMessage("Role name may contain letters, numbers, spaces, '-' and '_' only.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(64)
            .Matches(@"^[\p{L}\p{N} _\-]+$").WithMessage("Role name may contain letters, numbers, spaces, '-' and '_' only.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class SetRolePermissionsRequestValidator : AbstractValidator<SetRolePermissionsRequest>
{
    public SetRolePermissionsRequestValidator()
    {
        RuleFor(x => x.PermissionIds).NotEmpty().WithMessage("At least one permission is required.");
    }
}
