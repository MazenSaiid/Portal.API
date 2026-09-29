using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace Portal.Application.Common.Security;

internal static class IdentityErrors
{
    /// <summary>Turns Identity errors into a validation failure attached to <paramref name="field"/>.</summary>
    public static void ThrowIfFailed(this IdentityResult result, string field)
    {
        if (result.Succeeded) return;
        throw new ValidationException(result.Errors.Select(e => new ValidationFailure(field, e.Description)));
    }
}
