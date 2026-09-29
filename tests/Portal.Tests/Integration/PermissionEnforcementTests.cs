using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Roles;
using Portal.Application.Features.Users;
using Portal.Domain.Authorization;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class PermissionEnforcementTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // AC5
    public async Task User_without_permission_gets_403()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(); // no permissions
        var user = await admin.CreateUserAsync(role.Id);
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);

        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "any signed-in user can read their profile");
    }

    [Fact] // AC6 + AC7 — no re-login needed
    public async Task Granting_and_revoking_a_permission_takes_effect_on_the_next_request()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync();
        var user = await admin.CreateUserAsync(role.Id);
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);
        var viewUsers = await admin.GetPermissionIdAsync(Permissions.Users.View);

        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await Toggle(admin, role.Id, viewUsers, isGranted: true);
        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!.Permissions.Should().Contain(Permissions.Users.View);

        await Toggle(admin, role.Id, viewUsers, isGranted: false);
        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Any_of_several_permissions_is_enough_for_role_lookup()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(Permissions.Users.View);
        var user = await admin.CreateUserAsync(role.Id);
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);

        (await client.GetAsync("/api/roles/lookup")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact] // R7
    public async Task Deactivated_user_loses_access_immediately_with_an_existing_token()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(Permissions.Users.View);
        var user = await admin.CreateUserAsync(role.Id);
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);
        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PatchAsJsonAsync($"/api/users/{user.Id}/status", new UpdateUserStatusRequest(false))).EnsureSuccessStatusCode();

        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Moving_a_user_to_another_role_changes_their_permissions_immediately()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var viewer = await admin.CreateRoleAsync(Permissions.Users.View);
        var nothing = await admin.CreateRoleAsync();
        var user = await admin.CreateUserAsync(viewer.Id);
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);

        (await admin.PutAsJsonAsync($"/api/users/{user.Id}",
            new UpdateUserRequest(user.FirstName, user.LastName, user.Email, null, nothing.Id, true))).EnsureSuccessStatusCode();

        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task Toggle(HttpClient admin, Guid roleId, int permissionId, bool isGranted) =>
        (await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions",
            new SetRolePermissionsRequest([permissionId], isGranted))).EnsureSuccessStatusCode();
}
