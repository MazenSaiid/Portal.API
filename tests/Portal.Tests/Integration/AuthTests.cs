using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Users;
using Portal.Domain.Authorization;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class AuthTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // AC1
    public async Task Login_with_valid_credentials_returns_token_role_and_permissions()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(PortalApiFactory.AdminEmail, PortalApiFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        login!.AccessToken.Should().NotBeNullOrWhiteSpace();
        login.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        login.User.RoleName.Should().Be(SystemRoles.Administrator);
        login.User.Permissions.Should().BeEquivalentTo(PermissionRegistry.All.Select(p => p.Key));
    }

    [Theory] // AC2 — same message whether the email exists or not
    [InlineData(PortalApiFactory.AdminEmail, "Wrong@12345")]
    [InlineData("nobody@test.local", "Wrong@12345")]
    public async Task Login_with_bad_credentials_returns_401_with_generic_message(string email, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Detail.Should().Be("Invalid email or password.");
    }

    [Fact] // AC3
    public async Task Inactive_user_cannot_sign_in()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));
        (await admin.PatchAsJsonAsync($"/api/users/{user.Id}/status", new UpdateUserStatusRequest(false)))
            .EnsureSuccessStatusCode();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(user.Email, ApiClientExtensions.DefaultPassword));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("disabled");
    }

    [Fact] // A7
    public async Task Account_locks_after_five_failed_attempts()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "Wrong@12345"));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, ApiClientExtensions.DefaultPassword));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("locked");
    }

    [Theory] // AC4
    [InlineData("/api/auth/me")]
    [InlineData("/api/users")]
    [InlineData("/api/roles")]
    [InlineData("/api/permissions")]
    public async Task Protected_endpoints_without_token_return_401(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_rejects_wrong_current_password_and_accepts_correct_one()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);

        var wrong = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Nope@12345", "NewPassw0rd!"));
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("currentPassword");

        var ok = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(ApiClientExtensions.DefaultPassword, "NewPassw0rd!"));
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await factory.CreateAuthenticatedClientAsync(user.Email, "NewPassw0rd!"); // throws if login fails
    }
}
