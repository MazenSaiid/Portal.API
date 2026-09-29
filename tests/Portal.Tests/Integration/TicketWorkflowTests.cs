using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Tickets;
using Portal.Domain.Entities.Tickets;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class TicketWorkflowTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // TK4 + TK7 — the whole happy path, with timestamps (W6) and ordered history
    public async Task A_ticket_goes_through_its_full_life_and_every_step_is_recorded()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!;
        var ticket = await admin.CreateTicketAsync();

        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/assign", new AssignTicketRequest(me.Id));
        await admin.ChangeStatusAsync(ticket.Id, TicketStatus.InProgress);
        await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new TicketCommentRequest("Asked finance to reissue."));
        await admin.ChangeStatusAsync(ticket.Id, TicketStatus.OnHold, "Waiting for finance.");
        var resolved = await admin.ChangeStatusAsync(ticket.Id, TicketStatus.Resolved, "Invoice reissued.");
        resolved.ResolvedAt.Should().NotBeNull();
        resolved.AllowedStatuses.Should().BeEquivalentTo([TicketStatus.Open, TicketStatus.Closed]);

        var closed = await admin.ChangeStatusAsync(ticket.Id, TicketStatus.Closed);
        closed.ClosedAt.Should().NotBeNull();
        closed.AllowedStatuses.Should().Equal(TicketStatus.Open);

        var reopened = await admin.ChangeStatusAsync(ticket.Id, TicketStatus.Open, "Customer says it's still wrong.");
        reopened.ResolvedAt.Should().BeNull();
        reopened.ClosedAt.Should().BeNull();

        var history = await admin.GetHistoryAsync(ticket.Id);
        history.Select(h => h.Type).Should().Equal(
            TicketEventType.Created, TicketEventType.Assigned, TicketEventType.StatusChanged, // New → Open on assignment
            TicketEventType.StatusChanged, TicketEventType.Comment, TicketEventType.StatusChanged,
            TicketEventType.StatusChanged, TicketEventType.StatusChanged, TicketEventType.StatusChanged);
        history.Should().OnlyContain(h => h.CreatedByName == "System Administrator");
        history.Single(h => h.Type == TicketEventType.Comment).Message.Should().Be("Asked finance to reissue.");
        history[^1].Should().Match<TicketHistoryDto>(h => h.FromValue == "Closed" && h.ToValue == "Open");
    }

    [Theory] // W1
    [InlineData(TicketStatus.OnHold)] // New can't go straight to On hold
    [InlineData(TicketStatus.New)]    // same status
    public async Task Disallowed_transitions_are_rejected(TicketStatus target)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();

        var response = await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeStatusRequest(target, null), Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact] // W2
    public async Task In_progress_requires_an_assignee()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();

        var response = await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeStatusRequest(TicketStatus.InProgress, null), Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("Assign");
    }

    [Fact] // W3
    public async Task Resolving_requires_a_resolution_comment()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();

        var response = await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeStatusRequest(TicketStatus.Resolved, " "), Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("comment");
    }

    [Fact] // W5
    public async Task Closed_tickets_are_read_only_until_reopened()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var me = (await admin.GetFromJsonAsync<CurrentUserDto>("/api/auth/me"))!;
        var ticket = await admin.CreateTicketAsync();
        await admin.ChangeStatusAsync(ticket.Id, TicketStatus.Closed);
        var url = $"/api/tickets/{ticket.Id}";

        (await admin.PostAsJsonAsync($"{url}/comments", new TicketCommentRequest("late note"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync($"{url}/assign", new AssignTicketRequest(me.Id))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync($"{url}/escalate", new EscalateTicketRequest("why"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await admin.PutAsJsonAsync(url, new UpdateTicketRequest("x", "y", ticket.CategoryId, TicketPriority.Low, TicketChannel.Email), Json.Options))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await admin.ChangeStatusAsync(ticket.Id, TicketStatus.Open);
        (await admin.PostAsJsonAsync($"{url}/comments", new TicketCommentRequest("now it works"))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // E1 + E3 + TK6
    public async Task Escalation_raises_priority_and_de_escalation_keeps_it()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync(priority: TicketPriority.Low);
        var url = $"/api/tickets/{ticket.Id}";

        (await admin.PostAsJsonAsync($"{url}/escalate", new EscalateTicketRequest(""))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var escalated = await (await admin.PostAsJsonAsync($"{url}/escalate", new EscalateTicketRequest("Customer threatens to cancel")))
            .Content.ReadFromJsonAsync<TicketDto>(Json.Options);
        escalated!.IsEscalated.Should().BeTrue();
        escalated.Priority.Should().Be(TicketPriority.High);
        escalated.EscalationReason.Should().Be("Customer threatens to cancel");

        (await admin.PostAsJsonAsync($"{url}/escalate", new EscalateTicketRequest("again"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var calmed = await (await admin.PostAsJsonAsync($"{url}/de-escalate", new DeEscalateTicketRequest("Supervisor called back")))
            .Content.ReadFromJsonAsync<TicketDto>(Json.Options);
        calmed!.IsEscalated.Should().BeFalse();
        calmed.Priority.Should().Be(TicketPriority.High);

        var types = (await admin.GetHistoryAsync(ticket.Id)).Select(h => h.Type);
        types.Should().ContainInOrder(TicketEventType.Escalated, TicketEventType.PriorityChanged, TicketEventType.DeEscalated);
    }

    [Fact] // E1 — urgent stays urgent
    public async Task Escalation_never_lowers_priority()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync(priority: TicketPriority.Urgent);

        var escalated = await (await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new EscalateTicketRequest("Outage")))
            .Content.ReadFromJsonAsync<TicketDto>(Json.Options);

        escalated!.Priority.Should().Be(TicketPriority.Urgent);
    }

    [Fact] // E2
    public async Task Resolved_tickets_cannot_be_escalated()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();
        await admin.ChangeStatusAsync(ticket.Id, TicketStatus.Resolved, "Done");

        (await admin.PostAsJsonAsync($"/api/tickets/{ticket.Id}/escalate", new EscalateTicketRequest("late")))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
