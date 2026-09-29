using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Common.Models;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Users;
using Portal.Domain.Authorization;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class UsersTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // AC10
    public async Task Create_with_invalid_payload_returns_400_with_field_errors()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("", "", "not-an-email", "abc", "short", Guid.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.Should().BeEquivalentTo("firstName", "lastName", "email", "phoneNumber", "password", "roleId");
    }

    [Fact]
    public async Task Create_with_weak_password_returns_identity_policy_errors()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Weak", "Password", $"weak-{Guid.NewGuid():N}@test.local", null, "alllowercase", await admin.GetRoleIdAsync("Agent")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("password");
    }

    [Fact] // AC11
    public async Task Create_with_duplicate_email_returns_409_case_insensitively()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var agentId = await admin.GetRoleIdAsync("Agent");
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        await admin.CreateUserAsync(agentId, email);

        var response = await admin.PostAsJsonAsync("/api/users",
            new CreateUserRequest("Dup", "User", email.ToUpperInvariant(), null, ApiClientExtensions.DefaultPassword, agentId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_with_unknown_role_returns_400()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "No", "Role", $"norole-{Guid.NewGuid():N}@test.local", null, ApiClientExtensions.DefaultPassword, Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("roleId");
    }

    [Fact] // AC9
    public async Task List_supports_search_role_filter_and_paging()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++) await admin.CreateUserAsync(role.Id, $"{tag}-{i}@test.local");

        var page = await admin.GetFromJsonAsync<PagedResult<UserDto>>(
            $"/api/users?search={tag}&roleId={role.Id}&page=2&pageSize=2&sortBy=email&sortDirection=asc");

        page!.TotalCount.Should().Be(3);
        page.TotalPages.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Email.Should().Be($"{tag}-2@test.local");
        page.Items[0].RoleName.Should().Be(role.Name);
    }

    [Fact]
    public async Task Update_then_delete_user()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));

        var update = await admin.PutAsJsonAsync($"/api/users/{user.Id}",
            new UpdateUserRequest("Renamed", "Person", user.Email, "+966 500 000 000", user.RoleId!.Value, true));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<UserDto>();
        updated!.FullName.Should().Be("Renamed Person");
        updated.PhoneNumber.Should().Be("+966 500 000 000");

        (await admin.DeleteAsync($"/api/users/{user.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/users/{user.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reset_password_lets_the_user_sign_in_with_the_new_password()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));

        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest("Reset@2026x")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await factory.CreateAuthenticatedClientAsync(user.Email, "Reset@2026x");
    }

    [Theory] // R3
    [InlineData("delete")]
    [InlineData("deactivate")]
    [InlineData("change-role")]
    public async Task Admin_cannot_lock_themselves_out(string action)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");
        var agentId = await admin.GetRoleIdAsync("Agent");

        var response = action switch
        {
            "delete" => await admin.DeleteAsync($"/api/users/{me!.Id}"),
            "deactivate" => await admin.PatchAsJsonAsync($"/api/users/{me!.Id}/status", new UpdateUserStatusRequest(false)),
            _ => await admin.PutAsJsonAsync($"/api/users/{me!.Id}",
                new UpdateUserRequest(me.FirstName, me.LastName, me.Email, null, agentId, true)),
        };

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact] // R4
    public async Task Last_active_administrator_cannot_be_deactivated_by_another_user()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");
        var managerRole = await admin.CreateRoleAsync(Permissions.Users.View, Permissions.Users.Edit, Permissions.Users.Delete);
        var manager = await admin.CreateUserAsync(managerRole.Id);
        var managerClient = await factory.CreateAuthenticatedClientAsync(manager.Email, ApiClientExtensions.DefaultPassword);

        var deactivate = await managerClient.PatchAsJsonAsync($"/api/users/{me!.Id}/status", new UpdateUserStatusRequest(false));
        var delete = await managerClient.DeleteAsync($"/api/users/{me.Id}");

        deactivate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        delete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
