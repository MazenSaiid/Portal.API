using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Permissions;
using Portal.Application.Features.Roles;
using Portal.Application.Features.Users;

namespace Portal.Tests.Infrastructure;

/// <summary>Small helpers that keep the tests focused on behaviour instead of HTTP plumbing.</summary>
public static class ApiClientExtensions
{
    public const string DefaultPassword = "Passw0rd!";

    public static async Task<HttpClient> CreateAuthenticatedClientAsync(
        this PortalApiFactory factory, string email = PortalApiFactory.AdminEmail, string password = PortalApiFactory.AdminPassword)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    public static async Task<RoleDto> CreateRoleAsync(this HttpClient admin, params string[] permissionKeys)
    {
        var catalog = await admin.GetFromJsonAsync<List<PermissionModuleDto>>("/api/permissions");
        var ids = catalog!.SelectMany(m => m.Permissions).Where(p => permissionKeys.Contains(p.Key)).Select(p => p.Id).ToList();
        ids.Should().HaveCount(permissionKeys.Length, "every requested permission key must exist");

        var response = await admin.PostAsJsonAsync("/api/roles",
            new CreateRoleRequest($"Role {Guid.NewGuid():N}"[..20], "test role", ids));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoleDto>())!;
    }

    public static async Task<UserDto> CreateUserAsync(this HttpClient admin, Guid roleId, string? email = null)
    {
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(
            "Test", "User", email ?? $"user-{Guid.NewGuid():N}@test.local", null, DefaultPassword, roleId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    public static async Task<Guid> GetRoleIdAsync(this HttpClient admin, string roleName)
    {
        var roles = await admin.GetFromJsonAsync<List<RoleLookupDto>>("/api/roles/lookup");
        return roles!.Single(r => r.Name == roleName).Id;
    }

    public static async Task<int> GetPermissionIdAsync(this HttpClient admin, string key)
    {
        var catalog = await admin.GetFromJsonAsync<List<PermissionModuleDto>>("/api/permissions");
        return catalog!.SelectMany(m => m.Permissions).Single(p => p.Key == key).Id;
    }
}
