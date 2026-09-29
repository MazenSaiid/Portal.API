using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Portal.Application.Features.Roles;
using Portal.Domain.Authorization;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class RolesTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // AC14
    public async Task Role_permissions_list_the_full_catalogue_grouped_by_module()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(Permissions.Users.View);

        var result = await admin.GetFromJsonAsync<RolePermissionsDto>($"/api/roles/{role.Id}/permissions");

        result!.Modules.Select(m => m.Module).Should().Equal(PermissionRegistry.All.Select(p => p.Module).Distinct());
        var all = result.Modules.SelectMany(m => m.Permissions).ToList();
        all.Should().HaveCount(PermissionRegistry.All.Count);
        all.Where(p => p.IsGranted).Select(p => p.Key).Should().Equal(Permissions.Users.View);
    }

    [Fact] // AC14 — module toggle
    public async Task Granting_a_whole_module_then_revoking_it()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync();
        var perms = await admin.GetFromJsonAsync<RolePermissionsDto>($"/api/roles/{role.Id}/permissions");
        var usersModule = perms!.Modules.Single(m => m.Module == "Users").Permissions.Select(p => p.Id).ToList();

        var granted = await (await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions",
            new SetRolePermissionsRequest(usersModule, true))).Content.ReadFromJsonAsync<RolePermissionsDto>();
        granted!.Modules.Single(m => m.Module == "Users").Permissions.Should().OnlyContain(p => p.IsGranted);
        granted.Modules.Single(m => m.Module == "Roles").Permissions.Should().OnlyContain(p => !p.IsGranted);

        // Granting again is idempotent.
        (await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions", new SetRolePermissionsRequest(usersModule, true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var revoked = await (await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions",
            new SetRolePermissionsRequest(usersModule, false))).Content.ReadFromJsonAsync<RolePermissionsDto>();
        revoked!.Modules.SelectMany(m => m.Permissions).Should().OnlyContain(p => !p.IsGranted);
    }

    [Fact] // R2
    public async Task Duplicate_role_name_returns_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var response = await admin.PostAsJsonAsync("/api/roles", new CreateRoleRequest("agent", null, null));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact] // R5
    public async Task System_role_cannot_be_deleted_renamed_or_have_permissions_changed()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var adminRoleId = await admin.GetRoleIdAsync(SystemRoles.Administrator);
        var permissionId = await admin.GetPermissionIdAsync(Permissions.Users.View);

        (await admin.DeleteAsync($"/api/roles/{adminRoleId}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PutAsJsonAsync($"/api/roles/{adminRoleId}", new UpdateRoleRequest("Boss", null)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PutAsJsonAsync($"/api/roles/{adminRoleId}/permissions", new SetRolePermissionsRequest([permissionId], false)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // The description of a system role may still be edited.
        (await admin.PutAsJsonAsync($"/api/roles/{adminRoleId}", new UpdateRoleRequest(SystemRoles.Administrator, "Owners")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] // R6
    public async Task Role_with_users_cannot_be_deleted_but_empty_role_can()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var used = await admin.CreateRoleAsync();
        await admin.CreateUserAsync(used.Id);
        var empty = await admin.CreateRoleAsync();

        (await admin.DeleteAsync($"/api/roles/{used.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/roles/{empty.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/roles/{empty.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // R8
    public async Task Unknown_permission_ids_are_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync();

        var response = await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions", new SetRolePermissionsRequest([999_999], true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Role_list_reports_user_and_permission_counts()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(Permissions.Users.View, Permissions.Roles.View);
        await admin.CreateUserAsync(role.Id);

        var roles = await admin.GetFromJsonAsync<List<RoleDto>>($"/api/roles?search={Uri.EscapeDataString(role.Name)}");

        roles.Should().ContainSingle().Which.Should().Match<RoleDto>(r => r.UserCount == 1 && r.PermissionCount == 2);
    }
}
