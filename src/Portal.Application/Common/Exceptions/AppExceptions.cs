namespace Portal.Application.Common.Exceptions;

/// <summary>The requested resource does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.");

/// <summary>The request conflicts with the current state, e.g. a duplicate value (HTTP 409).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The request is valid but breaks a business rule (HTTP 422).</summary>
public sealed class BusinessRuleException(string message) : Exception(message);

/// <summary>The user is signed in and has the permission, but not for this particular item (HTTP 403).</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Credentials are missing, wrong, or the account may not sign in (HTTP 401).</summary>
public sealed class AuthenticationFailedException(string message) : Exception(message);
