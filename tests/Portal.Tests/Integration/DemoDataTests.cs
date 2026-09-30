using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Portal.Application.Features.Auth;
using Portal.Application.Features.Sla;
using Portal.Domain.Entities.Sla;
using Portal.Domain.Entities.Tickets;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Seeding;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

/// <summary>The demo data set (docs/demo.md) is consistent with the business rules it demonstrates.</summary>
public sealed class DemoDataTests : IClassFixture<PortalApiFactory>, IAsyncLifetime
{
    private readonly PortalApiFactory _factory;

    public DemoDataTests(PortalApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _factory.CreateClient().Dispose(); // starts the host, which runs the normal initializer
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Customers.AnyAsync())
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Demo_accounts_sign_in_with_the_demo_password_and_the_deactivated_one_is_refused()
    {
        foreach (var email in new[] { "sara.khan@portal.local", "omar.haddad@portal.local", "karim.aziz@portal.local" })
            (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DemoDataSeeder.DemoPassword)))
                .StatusCode.Should().Be(HttpStatusCode.OK, email);

        (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("hana.ibrahim@portal.local", DemoDataSeeder.DemoPassword)))
            .IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Tickets_cover_every_sla_state_and_status()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tickets = await db.Tickets.AsNoTracking().ToListAsync();
        var now = DateTime.UtcNow;
        var states = tickets
            .Select(t => SlaCalculator.State(t.CreatedAt, t.ResolutionDueAt, t.ResolvedAt ?? t.ClosedAt, now))
            .ToHashSet();

        states.Should().Contain([SlaState.OnTrack, SlaState.AtRisk, SlaState.Breached, SlaState.Met]);
        tickets.Select(t => t.Status).Distinct().Should().BeEquivalentTo(Enum.GetValues<TicketStatus>());
        tickets.Should().Contain(t => t.IsEscalated && t.EscalationReason!.StartsWith("Automatic"), "a rule escalated one");
        tickets.Should().Contain(t => t.AssigneeId == null, "the queue is not empty");
        (await db.CustomerAttachments.CountAsync()).Should().BeGreaterThan(0);
        (await db.AuditLogs.CountAsync(a => a.Action == Domain.Entities.Auditing.AuditAction.LockedOut)).Should().Be(1);
    }

    [Fact]
    public async Task Rules_already_ran_where_they_match_so_the_monitor_only_picks_up_the_ticket_left_for_the_demo()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var fired = await scope.ServiceProvider.GetRequiredService<ISlaEngine>().RunAsync(DateTime.UtcNow);

        fired.Should().Be(1, "only the urgent unanswered SMS ticket is left for the SLA monitor");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Tickets.SingleAsync(t => t.Subject == "Appointment reminders not sending SMS")).IsEscalated.Should().BeTrue();
    }
}
