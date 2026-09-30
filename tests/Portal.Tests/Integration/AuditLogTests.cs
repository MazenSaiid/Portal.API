using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Common.Models;
using Portal.Application.Features.Auditing;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Roles;
using Portal.Application.Features.Users;
using Portal.Domain.Authorization;
using Portal.Domain.Entities.Auditing;
using Portal.Infrastructure.Persistence;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class AuditLogTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // AL1
    public async Task Customer_create_update_delete_are_logged_with_user_ip_and_changes()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync(CustomerTestData.NewCustomer(name: "Audit Co"));
        await admin.PutAsJsonAsync($"/api/customers/{customer.Id}", CustomerTestData.NewCustomer(customer.Email, "Audit Company") with { City = "Dammam" }, Json.Options);
        await admin.DeleteAsync($"/api/customers/{customer.Id}");

        var entries = await LogsAsync(admin, $"entityType=Customer&entityId={customer.Id}");

        entries.Select(e => e.Action).Should().Equal(AuditAction.Deleted, AuditAction.Updated, AuditAction.Created);
        entries.Should().OnlyContain(e => e.UserName == "System Administrator" && e.UserId != null && e.IpAddress != null);
        var update = entries.Single(e => e.Action == AuditAction.Updated);
        update.Summary.Should().Be($"Customer Audit Company ({customer.Code}) updated");
        update.Changes.Should().BeEquivalentTo([
            new AuditChange("Name", "Audit Co", "Audit Company"),
            new AuditChange("City", "Riyadh", "Dammam"),
        ]);
        entries.Single(e => e.Action == AuditAction.Created).Changes.Should().Contain(c => c.Field == "Name" && c.To == "Audit Co");
    }

    [Fact] // AL1 — tickets keep enum names readable
    public async Task Ticket_changes_are_logged_with_readable_values()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();
        await admin.ChangeStatusAsync(ticket.Id, Domain.Entities.Tickets.TicketStatus.Resolved, "Done");

        var update = (await LogsAsync(admin, $"entityType=Ticket&entityId={ticket.Id}&action=Updated")).First();

        update.Changes.Should().Contain(c => c.Field == "Status" && c.From == "New" && c.To == "Resolved");
        update.Summary.Should().Contain(ticket.Code);
    }

    [Fact] // AL2
    public async Task Permission_grants_and_role_assignments_are_logged_with_names()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync();
        var permissionId = await admin.GetPermissionIdAsync(Permissions.Users.View);
        await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions", new SetRolePermissionsRequest([permissionId], true));
        await admin.PutAsJsonAsync($"/api/roles/{role.Id}/permissions", new SetRolePermissionsRequest([permissionId], false));
        var user = await admin.CreateUserAsync(role.Id);

        var perms = await LogsAsync(admin, "entityType=RolePermission");
        perms.Should().Contain(e => e.Summary == $"Permission Users.View granted to role {role.Name}" && e.Action == AuditAction.Created);
        perms.Should().Contain(e => e.Summary == $"Permission Users.View revoked from role {role.Name}" && e.Action == AuditAction.Deleted);

        var assignments = await LogsAsync(admin, "entityType=UserRole");
        assignments.Should().Contain(e => e.Summary == $"User {user.Email} assigned to role {role.Name}");
    }

    [Fact] // AL3
    public async Task Security_events_are_logged()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));
        var anonymous = factory.CreateClient();

        await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost@nowhere.test", "Whatever1!"));
        for (var i = 0; i < 5; i++)
            await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "Wrong@12345"));
        await admin.PostAsJsonAsync($"/api/users/{user.Id}/reset-password", new ResetPasswordRequest("Reset@2026x"));
        var login = await (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "Reset@2026x")))
            .Content.ReadFromJsonAsync<LoginResponse>();
        await anonymous.PostAsJsonAsync("/api/auth/logout", new RefreshTokenRequest(login!.RefreshToken));

        var security = await LogsAsync(admin, "entityType=Security", pageSize: 100);
        security.Should().Contain(e => e.Action == AuditAction.SignInFailed && e.UserName == "ghost@nowhere.test");
        security.Where(e => e.Action == AuditAction.SignInFailed && e.UserId == user.Id).Should().HaveCount(5);
        security.Should().Contain(e => e.Action == AuditAction.LockedOut && e.UserId == user.Id);
        security.Should().Contain(e => e.Action == AuditAction.SignedIn && e.UserId == user.Id);
        security.Should().Contain(e => e.Action == AuditAction.SignedOut && e.UserId == user.Id);
        (await LogsAsync(admin, $"action=PasswordReset&entityId={user.Id}")).Should().ContainSingle()
            .Which.UserName.Should().Be("System Administrator", "the admin did the reset");
    }

    [Fact] // AL4 + AL5
    public async Task Secrets_and_noise_fields_never_reach_the_log()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var user = await admin.CreateUserAsync(await admin.GetRoleIdAsync("Agent"));
        await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword); // updates LastLoginAt etc.

        var all = await LogsAsync(admin, $"entityType=User&entityId={user.Id}", pageSize: 100);

        all.Should().ContainSingle(e => e.Action == AuditAction.Created, "signing in only touches excluded fields");
        var fields = all.SelectMany(e => e.Changes).Select(c => c.Field).ToList();
        fields.Should().NotContain(["PasswordHash", "SecurityStamp", "ConcurrencyStamp", "NormalizedEmail", "LastLoginAt"]);
        fields.Should().Contain("Email");
    }

    [Fact] // AL6
    public async Task Log_can_be_filtered_by_text_action_user_and_date()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!;
        var marker = $"Filter{Guid.NewGuid():N}"[..14];
        await admin.CreateCustomerAsync(CustomerTestData.NewCustomer(name: marker));

        (await LogsAsync(admin, $"search={marker}")).Should().ContainSingle().Which.Action.Should().Be(AuditAction.Created);
        (await LogsAsync(admin, $"search={marker}&action=Deleted")).Should().BeEmpty();
        (await LogsAsync(admin, $"search={marker}&userId={me.Id}")).Should().ContainSingle();
        var future = Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"));
        (await LogsAsync(admin, $"search={marker}&from={future}")).Should().BeEmpty();

        var types = await admin.GetFromJsonAsync<List<string>>("/api/audit-logs/entity-types");
        types.Should().Contain(["Customer", "Security"]);
    }

    [Fact] // AL7
    public async Task Audit_entries_cannot_be_changed_or_deleted()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        await admin.CreateCustomerAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogs.OrderByDescending(l => l.Id).FirstAsync();

        entry.Summary = "tampered";
        var update = () => db.SaveChangesAsync();
        await update.Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");

        db.ChangeTracker.Clear();
        db.AuditLogs.Remove(await db.AuditLogs.FirstAsync(l => l.Id == entry.Id));
        await update.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // AL8
    public async Task Reading_the_log_requires_permission()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(Permissions.Users.View);
        var user = await admin.CreateUserAsync(role.Id);
        var client = await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);

        (await client.GetAsync("/api/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<List<AuditLogDto>> LogsAsync(HttpClient client, string query, int pageSize = 50) =>
        (await client.GetFromJsonAsync<PagedResult<AuditLogDto>>($"/api/audit-logs?pageSize={pageSize}&{query}", Json.Options))!.Items.ToList();
}
