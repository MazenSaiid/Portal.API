using System.Net.Http.Json;
using FluentAssertions;
using Portal.Application.Features.Tickets;
using Portal.Domain.Entities.Tickets;

namespace Portal.Tests.Infrastructure;

public static class TicketTestData
{
    public static async Task<int> GetCategoryIdAsync(this HttpClient client, string name = "General")
    {
        var categories = await client.GetFromJsonAsync<List<TicketCategoryDto>>("/api/ticket-categories", Json.Options);
        return categories!.Single(c => c.Name == name).Id;
    }

    public static async Task<TicketDto> CreateTicketAsync(this HttpClient client, int? customerId = null,
        TicketPriority priority = TicketPriority.Medium, Guid? assigneeId = null, string? subject = null)
    {
        customerId ??= (await client.CreateCustomerAsync()).Id;
        var request = new CreateTicketRequest(customerId.Value, subject ?? "Invoice shows the wrong amount",
            "The March invoice charges 500 units instead of 50.", await client.GetCategoryIdAsync(), priority,
            TicketChannel.Email, assigneeId);
        var response = await client.PostAsJsonAsync("/api/tickets", request, Json.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDto>(Json.Options))!;
    }

    public static async Task<TicketDto> ChangeStatusAsync(this HttpClient client, int ticketId, TicketStatus status, string? comment = null)
    {
        var response = await client.PostAsJsonAsync($"/api/tickets/{ticketId}/status", new ChangeStatusRequest(status, comment), Json.Options);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TicketDto>(Json.Options))!;
    }

    public static async Task<List<TicketHistoryDto>> GetHistoryAsync(this HttpClient client, int ticketId) =>
        (await client.GetFromJsonAsync<List<TicketHistoryDto>>($"/api/tickets/{ticketId}/history", Json.Options))!;
}
