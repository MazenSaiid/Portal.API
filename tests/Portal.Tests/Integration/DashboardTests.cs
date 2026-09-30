using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Tickets;
using Portal.Application.Features.Work;
using Portal.Domain.Authorization;
using Portal.Domain.Entities.Tickets;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class DashboardTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    private static readonly string[] AgentPermissions =
        [Permissions.Dashboard.View, Permissions.Tickets.View, Permissions.Tickets.Work, Permissions.Customers.View];

    [Fact] // AD1 + AD2
    public async Task Summary_and_my_tickets_reflect_the_agents_work_in_priority_order()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (agent, agentId) = await AgentAsync(admin);
        var customer = await admin.CreateCustomerAsync();

        var low = await admin.CreateTicketAsync(customer.Id, TicketPriority.Low, agentId, "Low one");
        var urgent = await admin.CreateTicketAsync(customer.Id, TicketPriority.Urgent, agentId, "Urgent one");
        var escalated = await admin.CreateTicketAsync(customer.Id, TicketPriority.Medium, agentId, "Escalated one");
        await admin.PostAsJsonAsync($"/api/tickets/{escalated.Id}/escalate", new EscalateTicketRequest("VIP"), Json.Options);
        await agent.ChangeStatusAsync(urgent.Id, TicketStatus.InProgress);
        var done = await admin.CreateTicketAsync(customer.Id, TicketPriority.High, agentId, "Resolved one");
        await agent.ChangeStatusAsync(done.Id, TicketStatus.Resolved, "Fixed");

        var dashboard = await GetDashboardAsync(agent);

        dashboard.CanViewTickets.Should().BeTrue();
        dashboard.Summary.Should().Match<DashboardSummaryDto>(s =>
            s.MyActive == 3 && s.InProgress == 1 && s.Escalated == 1 && s.HighPriority == 2 && s.ResolvedLast7Days == 1);
        dashboard.MyTickets.Select(t => t.Id).Should().Equal(escalated.Id, urgent.Id, low.Id);
        dashboard.MyTickets[0].Customer.Should().Match<DashboardCustomerDto>(c => c.Id == customer.Id && c.OtherActiveTickets == 2);
    }

    [Fact] // AD3
    public async Task Unassigned_queue_shows_waiting_tickets_most_urgent_first_and_they_can_be_taken()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (agent, agentId) = await AgentAsync(admin);
        var medium = await admin.CreateTicketAsync(priority: TicketPriority.Medium);
        var urgent = await admin.CreateTicketAsync(priority: TicketPriority.Urgent);

        var queue = (await GetDashboardAsync(agent)).UnassignedQueue.Select(t => t.Id).ToList();
        queue.IndexOf(urgent.Id).Should().BeLessThan(queue.IndexOf(medium.Id));

        (await agent.PostAsJsonAsync($"/api/tickets/{urgent.Id}/assign", new AssignTicketRequest(agentId))).EnsureSuccessStatusCode();

        var after = await GetDashboardAsync(agent);
        after.UnassignedQueue.Should().NotContain(t => t.Id == urgent.Id);
        after.MyTickets.Should().Contain(t => t.Id == urgent.Id);
    }

    [Fact] // AD4
    public async Task Team_activity_shows_what_others_did_on_my_tickets_but_not_my_own_actions()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (agent, agentId) = await AgentAsync(admin);
        var ticket = await admin.CreateTicketAsync(assigneeId: agentId);
        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new TicketCommentRequest("Customer called again"));
        await agent.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new TicketCommentRequest("My own note"));

        var dashboard = await GetDashboardAsync(agent);

        dashboard.TeamActivity.Should().Contain(a => a.TicketId == ticket.Id && a.Message == "Customer called again" && a.CreatedByName == "System Administrator");
        dashboard.TeamActivity.Should().NotContain(a => a.Message == "My own note");
        dashboard.Team.Should().Contain(m => m.Id == agentId && m.ActiveTickets == 1);
    }

    [Fact] // D1 / permissions
    public async Task Dashboard_requires_permission_and_hides_ticket_sections_without_ticket_access()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var noDashRole = await admin.CreateRoleAsync(Permissions.Tickets.View);
        var noDash = await Login(admin, noDashRole.Id);
        (await noDash.GetAsync("/api/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var tasksOnlyRole = await admin.CreateRoleAsync(Permissions.Dashboard.View);
        var tasksOnly = await Login(admin, tasksOnlyRole.Id);
        var dashboard = await GetDashboardAsync(tasksOnly);
        dashboard.CanViewTickets.Should().BeFalse();
        dashboard.MyTickets.Should().BeEmpty();
        dashboard.UnassignedQueue.Should().BeEmpty();
    }

    private static async Task<AgentDashboardDto> GetDashboardAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AgentDashboardDto>("/api/dashboard", Json.Options))!;

    private async Task<(HttpClient Client, Guid UserId)> AgentAsync(HttpClient admin)
    {
        var role = await admin.CreateRoleAsync(AgentPermissions);
        var user = await admin.CreateUserAsync(role.Id);
        return (await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword), user.Id);
    }

    private async Task<HttpClient> Login(HttpClient admin, Guid roleId)
    {
        var user = await admin.CreateUserAsync(roleId);
        return await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);
    }
}
