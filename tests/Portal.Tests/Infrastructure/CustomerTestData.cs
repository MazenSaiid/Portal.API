using System.Net.Http.Json;
using Portal.Application.Features.Customers;
using Portal.Domain.Entities.Customers;

namespace Portal.Tests.Infrastructure;

public static class CustomerTestData
{
    public static CustomerRequest NewCustomer(string? email = null, string? name = null) => new(
        CustomerType.Company,
        name ?? $"Acme {Guid.NewGuid():N}"[..14],
        email ?? $"c-{Guid.NewGuid():N}@acme.test",
        "+966 55 123 4567",
        ContactChannel.Email,
        PreferredLanguage.English,
        "King Fahd Rd 1",
        "Riyadh",
        "Saudi Arabia");

    public static async Task<CustomerDto> CreateCustomerAsync(this HttpClient client, CustomerRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/customers", request ?? NewCustomer(), Json.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerDto>(Json.Options))!;
    }
}
