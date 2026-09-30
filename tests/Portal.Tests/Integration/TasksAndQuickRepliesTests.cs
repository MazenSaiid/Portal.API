using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Features.Work;
using Portal.Domain.Authorization;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class TasksAndQuickRepliesTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    // ---------- Tasks ----------

    [Fact] // AD5
    public async Task Tasks_can_be_created_edited_completed_reopened_and_deleted()
    {
        var (me, _) = await UserAsync(Permissions.Dashboard.View);
        var due = DateTime.UtcNow.AddDays(1);

        var created = await (await me.PostAsJsonAsync("/api/tasks", new TaskRequest("Call Al Noor back", null, due, null, null), Json.Options))
            .Content.ReadFromJsonAsync<TaskDto>(Json.Options);
        created!.DueAt.Should().BeCloseTo(due, TimeSpan.FromSeconds(1));
        created.IsOverdue.Should().BeFalse();

        var edited = await (await me.PutAsJsonAsync($"/api/tasks/{created.Id}", new TaskRequest("Call Al Noor back re: quote", "Ask for PO number", due, null, null), Json.Options))
            .Content.ReadFromJsonAsync<TaskDto>(Json.Options);
        edited!.Notes.Should().Be("Ask for PO number");

        var done = await (await me.PostAsJsonAsync($"/api/tasks/{created.Id}/complete", new CompleteTaskRequest(true))).Content.ReadFromJsonAsync<TaskDto>(Json.Options);
        done!.IsDone.Should().BeTrue();
        done.CompletedAt.Should().NotBeNull();
        (await me.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Json.Options)).Should().NotContain(t => t.Id == created.Id, "open list hides done tasks");
        (await me.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=done", Json.Options)).Should().Contain(t => t.Id == created.Id);

        var reopened = await (await me.PostAsJsonAsync($"/api/tasks/{created.Id}/complete", new CompleteTaskRequest(false))).Content.ReadFromJsonAsync<TaskDto>(Json.Options);
        reopened!.CompletedAt.Should().BeNull();

        (await me.DeleteAsync($"/api/tasks/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await me.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=all", Json.Options)).Should().BeEmpty();
    }

    [Fact] // DT1 — a ticket link also links its customer; unknown links are rejected
    public async Task Linking_a_task_to_a_ticket_also_links_the_customer()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();

        var task = await (await admin.PostAsJsonAsync("/api/tasks", new TaskRequest("Follow up", null, null, ticket.Id, null), Json.Options))
            .Content.ReadFromJsonAsync<TaskDto>(Json.Options);
        task!.TicketCode.Should().Be(ticket.Code);
        task.CustomerId.Should().Be(ticket.Customer.Id);

        var bad = await admin.PostAsJsonAsync("/api/tasks", new TaskRequest("x", null, null, 999_999, null), Json.Options);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("ticketId");

        (await admin.PostAsJsonAsync("/api/tasks", new TaskRequest(" ", null, null, null, null), Json.Options))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // D6
    public async Task Deleting_a_linked_ticket_keeps_the_task_but_unlinks_it()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var ticket = await admin.CreateTicketAsync();
        var task = await (await admin.PostAsJsonAsync("/api/tasks", new TaskRequest("Check refund", null, null, ticket.Id, null), Json.Options))
            .Content.ReadFromJsonAsync<TaskDto>(Json.Options);

        (await admin.DeleteAsync($"/api/tickets/{ticket.Id}")).EnsureSuccessStatusCode();

        var tasks = await admin.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Json.Options);
        tasks!.Single(t => t.Id == task!.Id).TicketId.Should().BeNull();
    }

    [Fact] // DT2
    public async Task Other_users_cannot_see_or_touch_my_tasks()
    {
        var (me, _) = await UserAsync(Permissions.Dashboard.View);
        var (other, _) = await UserAsync(Permissions.Dashboard.View);
        var mine = await (await me.PostAsJsonAsync("/api/tasks", new TaskRequest("Private", null, null, null, null), Json.Options))
            .Content.ReadFromJsonAsync<TaskDto>(Json.Options);

        (await other.GetFromJsonAsync<List<TaskDto>>("/api/tasks?status=all", Json.Options)).Should().BeEmpty();
        (await other.PutAsJsonAsync($"/api/tasks/{mine!.Id}", new TaskRequest("Hacked", null, null, null, null), Json.Options))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/tasks/{mine.Id}/complete", new CompleteTaskRequest(true))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/tasks/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // AD6 / D3
    public async Task Reminders_count_overdue_and_due_today_tasks()
    {
        var (me, _) = await UserAsync(Permissions.Dashboard.View);
        var now = DateTime.UtcNow;
        await me.PostAsJsonAsync("/api/tasks", new TaskRequest("Overdue", null, now.AddHours(-2), null, null), Json.Options);
        await me.PostAsJsonAsync("/api/tasks", new TaskRequest("Later today", null, now.AddHours(1), null, null), Json.Options);
        await me.PostAsJsonAsync("/api/tasks", new TaskRequest("Next week", null, now.AddDays(7), null, null), Json.Options);
        await me.PostAsJsonAsync("/api/tasks", new TaskRequest("No date", null, null, null, null), Json.Options);

        var endOfDay = now.AddHours(3).ToString("O");
        var reminders = await me.GetFromJsonAsync<RemindersDto>($"/api/tasks/reminders?endOfDay={Uri.EscapeDataString(endOfDay)}");

        reminders.Should().Be(new RemindersDto(Overdue: 1, DueToday: 1));
        var open = await me.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Json.Options);
        open!.Select(t => t.Title).Should().Equal("Overdue", "Later today", "Next week", "No date");
        open[0].IsOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task Tasks_require_the_dashboard_permission()
    {
        var (noDash, _) = await UserAsync(Permissions.Tickets.View);
        (await noDash.GetAsync("/api/tasks")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- Quick replies ----------

    [Fact] // AD7 / D4
    public async Task Agents_see_seeded_shared_replies_and_their_own()
    {
        var (agent, _) = await UserAsync(Permissions.Tickets.Work);
        var (other, _) = await UserAsync(Permissions.Tickets.Work);
        await other.PostAsJsonAsync("/api/quick-replies", new QuickReplyRequest("Other's", "Hidden", false));

        var created = await (await agent.PostAsJsonAsync("/api/quick-replies", new QuickReplyRequest("My sign-off", "Thanks, {agent}", false)))
            .Content.ReadFromJsonAsync<QuickReplyDto>();

        var replies = await agent.GetFromJsonAsync<List<QuickReplyDto>>("/api/quick-replies");
        replies!.Where(r => r.IsShared).Select(r => r.Title).Should().Contain(["Acknowledge request", "Ask for more details", "Confirm resolution"]);
        replies.Where(r => r.IsShared).Should().OnlyContain(r => !r.CanEdit, "agents can't edit shared replies");
        replies.Should().Contain(r => r.Id == created!.Id && !r.IsShared && r.CanEdit);
        replies.Should().NotContain(r => r.Title == "Other's");
    }

    [Fact] // QR2
    public async Task Shared_replies_need_the_manage_permission_and_personal_ones_their_owner()
    {
        var (agent, _) = await UserAsync(Permissions.Tickets.Work);
        var (other, _) = await UserAsync(Permissions.Tickets.Work);
        var admin = await factory.CreateAuthenticatedClientAsync();

        (await agent.PostAsJsonAsync("/api/quick-replies", new QuickReplyRequest("Team", "Body", true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var shared = await (await admin.PostAsJsonAsync("/api/quick-replies", new QuickReplyRequest("Team greeting", "Hi {customer}", true)))
            .Content.ReadFromJsonAsync<QuickReplyDto>();
        (await agent.PutAsJsonAsync($"/api/quick-replies/{shared!.Id}", new QuickReplyRequest("Changed", "x", true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.DeleteAsync($"/api/quick-replies/{shared.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var personal = await (await agent.PostAsJsonAsync("/api/quick-replies", new QuickReplyRequest("Mine", "x", false))).Content.ReadFromJsonAsync<QuickReplyDto>();
        (await other.DeleteAsync($"/api/quick-replies/{personal!.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await agent.DeleteAsync($"/api/quick-replies/{personal.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await admin.PostAsJsonAsync("/api/quick-replies", new QuickReplyRequest("", "", false))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "QR1");
    }

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(params string[] permissions)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await admin.CreateRoleAsync(permissions);
        var user = await admin.CreateUserAsync(role.Id);
        return (await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword), user.Id);
    }
}
