using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Users;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class RefreshTokenTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    private readonly HttpClient _anonymous = factory.CreateClient();

    [Fact] // RT1 + RT2
    public async Task Refresh_rotates_the_token_pair_and_the_new_access_token_works()
    {
        var (_, login) = await NewUserSessionAsync();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.RefreshTokenExpiresAt.Should().BeAfter(login.ExpiresAt);

        var refreshed = await RefreshAsync(login.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = (await refreshed.Content.ReadFromJsonAsync<LoginResponse>())!;
        next.RefreshToken.Should().NotBe(login.RefreshToken);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", next.AccessToken);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] // RT3 / S2
    public async Task Reusing_a_rotated_token_is_rejected_and_revokes_the_newer_one_too()
    {
        var (_, login) = await NewUserSessionAsync();
        var next = (await (await RefreshAsync(login.RefreshToken)).Content.ReadFromJsonAsync<LoginResponse>())!;

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(next.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "reuse ends every session");
    }

    [Fact]
    public async Task Unknown_or_empty_refresh_token_is_rejected()
    {
        (await RefreshAsync("not-a-real-token")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync("")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // RT4
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        var (_, login) = await NewUserSessionAsync();

        (await _anonymous.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(login.RefreshToken)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _anonymous.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(login.RefreshToken)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // RT5
    public async Task Deactivated_user_cannot_refresh()
    {
        var (user, login) = await NewUserSessionAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        (await admin.PatchAsJsonAsync($"/api/users/{user.Id}/status", new UpdateUserStatusRequest(false))).EnsureSuccessStatusCode();

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // RT5
    public async Task Admin_password_reset_ends_the_users_sessions()
    {
        var (user, login) = await NewUserSessionAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest("Reset@2026x")))
            .EnsureSuccessStatusCode();

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // RT6 / S5
    public async Task Change_password_returns_a_new_session_and_revokes_the_old_one()
    {
        var (user, login) = await NewUserSessionAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(ApiClientExtensions.DefaultPassword, "Changed@2026x"));
        var fresh = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(fresh.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> RefreshAsync(string token) =>
        _anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequest(token));

    private async Task<(UserDto User, LoginResponse Login)> NewUserSessionAsync()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));
        var response = await _anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, ApiClientExtensions.DefaultPassword));
        response.EnsureSuccessStatusCode();
        return (user, (await response.Content.ReadFromJsonAsync<LoginResponse>())!);
    }
}
