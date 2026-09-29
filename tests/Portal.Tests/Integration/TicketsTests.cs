using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Common.Models;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Tickets;
using Portal.Domain.Authorization;
using Portal.Domain.Entities.Tickets;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class TicketsTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // TK1
    public async Task Creating_a_ticket_persists_every_field_and_records_history()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();

        var ticket = await admin.CreateTicketAsync(customer.Id, TicketPriority.High);

        ticket.Code.Should().Be($"TCK-{ticket.Id:D5}");
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.Priority.Should().Be(TicketPriority.High);
        ticket.CategoryName.Should().Be("General");
        ticket.Customer.Id.Should().Be(customer.Id);
        ticket.CreatedByName.Should().Be("System Administrator");
        ticket.AllowedStatuses.Should().BeEquivalentTo([TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed]);

        var history = await admin.GetHistoryAsync(ticket.Id);
        history.Should().ContainSingle().Which.Type.Should().Be(TicketEventType.Created);
    }

    [Fact] // TK1 + W4
    public async Task Creating_with_an_assignee_starts_open()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");

        var ticket = await admin.CreateTicketAsync(assigneeId: me!.Id);

        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.AssigneeName.Should().Be("System Administrator");
        (await admin.GetHistoryAsync(ticket.Id)).Select(h => h.Type).Should().Equal(TicketEventType.Created, TicketEventType.Assigned);
    }

    [Fact] // T1
    public async Task Invalid_ticket_returns_field_errors()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var response = await admin.PostAsJsonAsync("/api/tickets",
            new CreateTicketRequest(0, "", "", 0, TicketPriority.Low, TicketChannel.Email), Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys
            .Should().Contain(["customerId", "subject", "description", "categoryId"]);
    }

    [Fact] // T2
    public async Task Tickets_cannot_be_opened_for_an_inactive_customer()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync(CustomerTestData.NewCustomer() with { IsActive = false });

        var response = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(customer.Id, "Help", "Please help",
            await admin.GetCategoryIdAsync(), TicketPriority.Low, TicketChannel.Phone), Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact] // TK2
    public async Task List_filters_by_status_priority_assignee_customer_and_escalation()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!;
        var customer = await admin.CreateCustomerAsync();
        var urgentMine = await admin.CreateTicketAsync(customer.Id, TicketPriority.Urgent, me.Id);
        var lowUnassigned = await admin.CreateTicketAsync(customer.Id, TicketPriority.Low);
        var resolved = await admin.CreateTicketAsync(customer.Id, TicketPriority.Medium);
        await admin.ChangeStatusAsync(resolved.Id, TicketStatus.Resolved, "Fixed the invoice.");
        await admin.PostAsJsonAsync($"/api/tickets/{lowUnassigned.Id}/escalate", new EscalateTicketRequest("VIP customer"), Json.Options);

        async Task<List<int>> Ids(string query) =>
            (await admin.GetFromJsonAsync<PagedResult<TicketListItemDto>>($"/api/tickets?customerId={customer.Id}&{query}", Json.Options))!
            .Items.Select(t => t.Id).ToList();

        (await Ids("status=New&status=Open")).Should().BeEquivalentTo([urgentMine.Id, lowUnassigned.Id]);
        (await Ids("status=Resolved")).Should().Equal(resolved.Id);
        (await Ids("priority=Urgent")).Should().Equal(urgentMine.Id);
        (await Ids("assignedTo=me")).Should().Equal(urgentMine.Id);
        (await Ids("assignedTo=unassigned")).Should().BeEquivalentTo([lowUnassigned.Id, resolved.Id]);
        (await Ids("escalated=true")).Should().Equal(lowUnassigned.Id);
        (await Ids($"search={urgentMine.Code}")).Should().Equal(urgentMine.Id);
        (await Ids("sortBy=priority&sortDirection=desc")).First().Should().Be(urgentMine.Id, "priority sorts by severity, not alphabetically");
    }

    [Fact] // TK3
    public async Task Editing_records_each_change_in_history()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync(priority: TicketPriority.Low);
        var billing = await admin.GetCategoryIdAsync("Billing");

        var response = await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}",
            new UpdateTicketRequest("Wrong invoice amount (March)", ticket.Description, billing, TicketPriority.High, TicketChannel.Phone), Json.Options);
        var updated = (await response.Content.ReadFromJsonAsync<TicketDto>(Json.Options))!;

        updated.CategoryName.Should().Be("Billing");
        updated.Priority.Should().Be(TicketPriority.High);
        var history = await admin.GetHistoryAsync(ticket.Id);
        history.Should().Contain(h => h.Type == TicketEventType.CategoryChanged && h.FromValue == "General" && h.ToValue == "Billing");
        history.Should().Contain(h => h.Type == TicketEventType.PriorityChanged && h.FromValue == "Low" && h.ToValue == "High");
        history.Should().Contain(h => h.Type == TicketEventType.Updated && h.Message == "Changed subject, channel.");
    }

    [Fact]
    public async Task Delete_ticket_and_customers_with_tickets_are_protected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var ticket = await admin.CreateTicketAsync(customer.Id);

        (await admin.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "K7");

        (await admin.DeleteAsync($"/api/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---------- Assignment (A1–A3) ----------

    [Fact] // A2
    public async Task Agent_can_take_an_unassigned_ticket_and_release_it_but_not_assign_others()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (agent, agentId) = await AgentAsync(admin);
        var (_, otherAgentId) = await AgentAsync(admin);
        var ticket = await admin.CreateTicketAsync();
        var url = $"/api/tickets/{ticket.Id}/assign";

        (await agent.PostAsJsonAsync(url, new AssignTicketRequest(otherAgentId))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var taken = await (await agent.PostAsJsonAsync(url, new AssignTicketRequest(agentId))).Content.ReadFromJsonAsync<TicketDto>(Json.Options);
        taken!.AssigneeId.Should().Be(agentId);
        taken.Status.Should().Be(TicketStatus.Open, "W4");

        (await agent.PostAsJsonAsync(url, new AssignTicketRequest(null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync(url, new AssignTicketRequest(otherAgentId))).StatusCode.Should().Be(HttpStatusCode.OK, "A1");
        (await agent.PostAsJsonAsync(url, new AssignTicketRequest(agentId))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "already someone else's");

        var history = await admin.GetHistoryAsync(ticket.Id);
        history.Where(h => h.Type is TicketEventType.Assigned or TicketEventType.Unassigned).Should().HaveCount(3);
    }

    [Fact] // A3 + K4
    public async Task Only_active_agents_can_be_assigned_and_are_listed()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var viewerRole = await admin.CreateRoleAsync(Permissions.Tickets.View);
        var viewer = await admin.CreateUserAsync(viewerRole.Id);
        var (_, agentId) = await AgentAsync(admin);
        var ticket = await admin.CreateTicketAsync();

        var response = await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketRequest(viewer.Id));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var assignees = await admin.GetFromJsonAsync<List<AssigneeDto>>("/api/tickets/assignees");
        assignees!.Select(a => a.Id).Should().Contain(agentId).And.NotContain(viewer.Id);
    }

    [Fact] // TK9
    public async Task Ticket_endpoints_require_permissions()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();
        var viewerRole = await admin.CreateRoleAsync(Permissions.Tickets.View);
        var viewerUser = await admin.CreateUserAsync(viewerRole.Id);
        var viewer = await factory.CreateAuthenticatedClientAsync(viewerUser.Email, ApiClientExtensions.DefaultPassword);

        (await viewer.GetAsync($"/api/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeStatusRequest(TicketStatus.Open, null), Json.Options))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new TicketCommentRequest("hi"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new EscalateTicketRequest("x"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/tickets/{ticket.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/ticket-categories", new TicketCategoryRequest("X", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- Categories (T3 / TK8) ----------

    [Fact]
    public async Task Categories_are_unique_and_protected_while_in_use()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var name = $"Cat {Guid.NewGuid():N}"[..12];
        var created = await (await admin.PostAsJsonAsync("/api/ticket-categories", new TicketCategoryRequest(name, "Temp")))
            .Content.ReadFromJsonAsync<TicketCategoryDto>();

        (await admin.PostAsJsonAsync("/api/ticket-categories", new TicketCategoryRequest(name.ToUpperInvariant(), null)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        await admin.CreateTicketAsync(); // puts "General" in use
        (await admin.DeleteAsync($"/api/ticket-categories/{await admin.GetCategoryIdAsync()}")).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "a category in use can't be deleted");

        (await admin.PutAsJsonAsync($"/api/ticket-categories/{created!.Id}", new TicketCategoryRequest(name, null, IsActive: false)))
            .EnsureSuccessStatusCode();
        var active = await admin.GetFromJsonAsync<List<TicketCategoryDto>>("/api/ticket-categories?activeOnly=true");
        active!.Should().NotContain(c => c.Id == created.Id);

        var customer = await admin.CreateCustomerAsync();
        (await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(customer.Id, "S", "D", created.Id, TicketPriority.Low, TicketChannel.Email), Json.Options))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "inactive categories can't be chosen");

        (await admin.DeleteAsync($"/api/ticket-categories/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<(HttpClient Client, Guid UserId)> AgentAsync(HttpClient admin)
    {
        var role = await admin.CreateRoleAsync(Permissions.Tickets.View, Permissions.Tickets.Work);
        var user = await admin.CreateUserAsync(role.Id);
        return (await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword), user.Id);
    }
}
