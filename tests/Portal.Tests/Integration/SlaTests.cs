using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Common.Models;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Sla;
using Portal.Application.Features.Tickets;
using Portal.Domain.Authorization;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;
using Portal.Infrastructure.Persistence;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

/// <summary>SLA targets, first response, policies, list filter and notifications.</summary>
public sealed class SlaTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // SA1
    public async Task New_tickets_get_due_dates_from_their_priority_and_priority_changes_recompute_them()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var urgent = await admin.CreateTicketAsync(priority: TicketPriority.Urgent);

        urgent.Sla.FirstResponseDueAt.Should().BeCloseTo(urgent.CreatedAt.AddMinutes(30), TimeSpan.FromSeconds(5));
        urgent.Sla.ResolutionDueAt.Should().BeCloseTo(urgent.CreatedAt.AddHours(4), TimeSpan.FromSeconds(5));
        urgent.Sla.FirstResponseState.Should().Be(SlaState.OnTrack);

        var lowered = await (await admin.PutAsJsonAsync($"/api/tickets/{urgent.Id}",
            new UpdateTicketRequest(urgent.Subject, urgent.Description, urgent.CategoryId, TicketPriority.Low, urgent.Channel), Json.Options))
            .Content.ReadFromJsonAsync<TicketDto>(Json.Options);
        lowered!.Sla.ResolutionDueAt.Should().BeCloseTo(urgent.CreatedAt.AddDays(5), TimeSpan.FromSeconds(5), "still measured from creation (S4)");
    }

    [Fact] // SA2
    public async Task The_first_comment_counts_as_the_first_response()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();
        ticket.Sla.FirstRespondedAt.Should().BeNull();

        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new TicketCommentRequest("Looking into it"));
        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new TicketCommentRequest("Second"));

        var after = await admin.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}", Json.Options);
        after!.Sla.FirstRespondedAt.Should().NotBeNull();
        after.Sla.FirstResponseState.Should().Be(SlaState.Met);
    }

    [Fact] // SA3 / SL1
    public async Task Targets_can_be_changed_by_sla_managers_only_and_are_validated()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var original = await admin.GetFromJsonAsync<List<SlaPolicyDto>>("/api/sla/policies", Json.Options);
        original.Should().HaveCount(4);

        var changed = original!.Select(p => p.Priority == TicketPriority.Medium ? p with { FirstResponseMinutes = 60, ResolutionMinutes = 120 } : p).ToList();
        (await admin.PutAsJsonAsync("/api/sla/policies", changed, Json.Options)).StatusCode.Should().Be(HttpStatusCode.OK);
        var ticket = await admin.CreateTicketAsync(priority: TicketPriority.Medium);
        ticket.Sla.ResolutionDueAt.Should().BeCloseTo(ticket.CreatedAt.AddMinutes(120), TimeSpan.FromSeconds(5));

        var invalid = original.Select(p => p with { ResolutionMinutes = 1, FirstResponseMinutes = 10 }).ToList();
        (await admin.PutAsJsonAsync("/api/sla/policies", invalid, Json.Options)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/sla/policies", original.Take(2).ToList(), Json.Options)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var viewerRole = await admin.CreateRoleAsync(Permissions.Tickets.View);
        var viewer = await admin.CreateUserAsync(viewerRole.Id);
        var client = await factory.CreateAuthenticatedClientAsync(viewer.Email, ApiClientExtensions.DefaultPassword);
        (await client.GetAsync("/api/sla/policies")).StatusCode.Should().Be(HttpStatusCode.OK, "agents may read targets");
        (await client.PutAsJsonAsync("/api/sla/policies", original, Json.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await admin.PutAsJsonAsync("/api/sla/policies", original, Json.Options); // restore
    }

    [Fact] // SA7
    public async Task Ticket_list_filters_breached_and_at_risk_tickets()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var breached = await admin.CreateTicketAsync(customer.Id);
        var atRisk = await admin.CreateTicketAsync(customer.Id);
        var fine = await admin.CreateTicketAsync(customer.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            await db.Tickets.Where(t => t.Id == breached.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ResolutionDueAt, now.AddMinutes(-5)));
            await db.Tickets.Where(t => t.Id == atRisk.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ResolutionAtRiskAt, now.AddMinutes(-5)).SetProperty(t => t.ResolutionDueAt, now.AddHours(1)));
        }

        async Task<List<int>> Ids(string sla) =>
            (await admin.GetFromJsonAsync<PagedResult<TicketListItemDto>>($"/api/tickets?customerId={customer.Id}&sla={sla}", Json.Options))!
            .Items.Select(t => t.Id).ToList();

        (await Ids("breached")).Should().Equal(breached.Id);
        (await Ids("atRisk")).Should().Equal(atRisk.Id);
        var list = await admin.GetFromJsonAsync<PagedResult<TicketListItemDto>>($"/api/tickets?customerId={customer.Id}", Json.Options);
        list!.Items.Single(t => t.Id == breached.Id).Sla.ResolutionState.Should().Be(SlaState.Breached);
        list.Items.Single(t => t.Id == fine.Id).Sla.ResolutionState.Should().Be(SlaState.OnTrack);
    }

    [Fact] // SA6 / SL4
    public async Task Assignment_and_escalation_notify_the_assignee_who_can_read_them()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(Permissions.Tickets.View, Permissions.Tickets.Work);
        var agentUser = await admin.CreateUserAsync(role.Id);
        var agent = await factory.CreateAuthenticatedClientAsync(agentUser.Email, ApiClientExtensions.DefaultPassword);
        var ticket = await admin.CreateTicketAsync();

        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketRequest(agentUser.Id));
        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new EscalateTicketRequest("VIP"));

        (await agent.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count"))!.Count.Should().Be(2);
        var list = await agent.GetFromJsonAsync<List<NotificationDto>>("/api/notifications", Json.Options);
        list!.Select(n => n.Type).Should().Equal(NotificationType.TicketEscalated, NotificationType.TicketAssigned);
        list.Should().OnlyContain(n => n.TicketId == ticket.Id);

        (await admin.PostAsync($"/api/notifications/{list[0].Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound, "not the admin's");
        (await agent.PostAsync($"/api/notifications/{list[0].Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await agent.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count"))!.Count.Should().Be(1);
        await agent.PostAsync("/api/notifications/read-all", null);
        (await agent.GetFromJsonAsync<List<NotificationDto>>("/api/notifications?unreadOnly=true", Json.Options)).Should().BeEmpty();

        // Acting on your own ticket doesn't notify yourself.
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!;
        var before = (await admin.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count"))!.Count;
        await admin.CreateTicketAsync(assigneeId: me.Id);
        (await admin.GetFromJsonAsync<UnreadCount>("/api/notifications/unread-count"))!.Count.Should().Be(before);
    }

    private sealed record UnreadCount(int Count);
}

/// <summary>Auto-assignment gets its own database so agent workloads are predictable.</summary>
public sealed class AutoAssignTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // SA4
    public async Task New_unassigned_tickets_go_to_the_least_loaded_agent_when_enabled()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!;
        var role = await admin.CreateRoleAsync(Permissions.Tickets.View, Permissions.Tickets.Work);
        var busy = await admin.CreateUserAsync(role.Id);
        var free = await admin.CreateUserAsync(role.Id);
        await admin.CreateTicketAsync(assigneeId: me.Id);
        await admin.CreateTicketAsync(assigneeId: busy.Id);

        (await admin.CreateTicketAsync()).AssigneeId.Should().BeNull("auto-assignment is off by default");

        (await admin.PutAsJsonAsync("/api/sla/settings", new AutomationSettingsDto(true))).EnsureSuccessStatusCode();
        var auto = await admin.CreateTicketAsync();

        auto.AssigneeId.Should().Be(free.Id);
        auto.Status.Should().Be(TicketStatus.Open);
        (await admin.GetHistoryAsync(auto.Id)).Should().Contain(h => h.Type == TicketEventType.Assigned && h.Message!.Contains("automatically"));

        var freeClient = await factory.CreateAuthenticatedClientAsync(free.Email, ApiClientExtensions.DefaultPassword);
        (await freeClient.GetFromJsonAsync<List<NotificationDto>>("/api/notifications", Json.Options))!
            .Should().ContainSingle(n => n.Type == NotificationType.TicketAssigned && n.TicketId == auto.Id);

        (await admin.PutAsJsonAsync("/api/sla/settings", new AutomationSettingsDto(false))).EnsureSuccessStatusCode();
    }
}

/// <summary>Escalation rules, evaluated by calling the engine with a chosen "now".</summary>
public sealed class SlaEngineTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // SA5
    public async Task A_breached_resolution_rule_escalates_and_notifies_supervisors_exactly_once()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var rule = await CreateRuleAsync(admin, new EscalationRuleRequest("Resolution missed", true, SlaTrigger.ResolutionBreached,
            null, null, Escalate: true, RaisePriorityTo: null, NotifyAssignee: false, NotifySupervisors: true));
        var ticket = await admin.CreateTicketAsync(priority: TicketPriority.Low);
        var before = await UnreadAsync(admin);

        (await RunEngineAsync(DateTime.UtcNow.AddHours(1))).Should().Be(0, "nothing is late yet");
        var fired = await RunEngineAsync(DateTime.UtcNow.AddDays(6));
        fired.Should().BeGreaterThan(0);

        var after = await admin.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}", Json.Options);
        after!.IsEscalated.Should().BeTrue();
        after.EscalationReason.Should().Contain("Resolution missed");
        after.Priority.Should().Be(TicketPriority.High, "automatic escalation raises priority like manual escalation");
        (await admin.GetHistoryAsync(ticket.Id)).Should().Contain(h => h.Type == TicketEventType.Escalated && h.CreatedByName == null);
        (await UnreadAsync(admin)).Should().BeGreaterThan(before, "the admin is a supervisor (Tickets.Assign)");

        (await RunEngineAsync(DateTime.UtcNow.AddDays(7))).Should().Be(0, "each rule fires once per ticket");
        var rules = await admin.GetFromJsonAsync<List<EscalationRuleDto>>("/api/sla/rules", Json.Options);
        rules!.Single(r => r.Id == rule.Id).TimesFired.Should().BeGreaterThan(0);

        await admin.DeleteAsync($"/api/sla/rules/{rule.Id}");
    }

    [Fact] // SA5 + SL3 + MinPriority
    public async Task Unassigned_rule_raises_priority_but_respects_the_minimum_priority_and_never_lowers()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var rule = await CreateRuleAsync(admin, new EscalationRuleRequest("Stuck in queue", true, SlaTrigger.UnassignedFor,
            30, TicketPriority.Medium, Escalate: false, RaisePriorityTo: TicketPriority.High, NotifyAssignee: false, NotifySupervisors: false));
        var low = await admin.CreateTicketAsync(priority: TicketPriority.Low);
        var medium = await admin.CreateTicketAsync(priority: TicketPriority.Medium);
        var urgent = await admin.CreateTicketAsync(priority: TicketPriority.Urgent);

        await RunEngineAsync(DateTime.UtcNow.AddMinutes(31));

        (await GetAsync(admin, low.Id)).Priority.Should().Be(TicketPriority.Low, "below the rule's minimum priority");
        var raised = await GetAsync(admin, medium.Id);
        raised.Priority.Should().Be(TicketPriority.High);
        raised.Sla.ResolutionDueAt.Should().BeCloseTo(medium.CreatedAt.AddDays(1), TimeSpan.FromSeconds(5), "due dates follow the new priority");
        (await GetAsync(admin, urgent.Id)).Priority.Should().Be(TicketPriority.Urgent, "never lowered (SL3)");

        await admin.DeleteAsync($"/api/sla/rules/{rule.Id}");
    }

    [Fact] // SL2
    public async Task Invalid_rules_are_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.PostAsJsonAsync("/api/sla/rules", new EscalationRuleRequest("No actions", true, SlaTrigger.ResolutionBreached,
            null, null, false, null, false, false), Json.Options)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/sla/rules", new EscalationRuleRequest("No threshold", true, SlaTrigger.UnassignedFor,
            null, null, true, null, false, false), Json.Options)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<EscalationRuleDto> CreateRuleAsync(HttpClient admin, EscalationRuleRequest request)
    {
        var response = await admin.PostAsJsonAsync("/api/sla/rules", request, Json.Options);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<EscalationRuleDto>(Json.Options))!;
    }

    private async Task<int> RunEngineAsync(DateTime now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISlaEngine>().RunAsync(now);
    }

    private static async Task<TicketDto> GetAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<TicketDto>($"/api/tickets/{id}", Json.Options))!;

    private static async Task<int> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Dictionary<string, int>>("/api/notifications/unread-count"))!["count"];
}
