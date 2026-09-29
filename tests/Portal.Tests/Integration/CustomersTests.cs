using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Common.Models;
using Portal.Application.Features.Customers;
using Portal.Domain.Authorization;
using Portal.Domain.Entities.Customers;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class CustomersTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    [Fact] // CM1
    public async Task Create_read_update_delete_customer()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var created = await admin.CreateCustomerAsync();
        created.Code.Should().Be($"CUS-{created.Id:D5}");
        created.CreatedByName.Should().Be("System Administrator", "audit fields are stamped automatically (C9)");

        var update = CustomerTestData.NewCustomer(created.Email, "Renamed Co") with { City = "Jeddah", IsActive = false };
        var updated = await (await admin.PutAsJsonAsync($"/api/customers/{created.Id}", update, Json.Options))
            .Content.ReadFromJsonAsync<CustomerDto>(Json.Options);
        updated!.Name.Should().Be("Renamed Co");
        updated.City.Should().Be("Jeddah");
        updated.IsActive.Should().BeFalse();
        updated.UpdatedByName.Should().Be("System Administrator");

        (await admin.DeleteAsync($"/api/customers/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/customers/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // CR1
    public async Task Invalid_customer_returns_field_errors()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var request = CustomerTestData.NewCustomer() with { Name = "", Email = null, Phone = "abc" };

        var response = await admin.PostAsJsonAsync("/api/customers", request, Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors;
        errors.Keys.Should().Contain(["name", "phone", "preferredChannel"]);
    }

    [Fact] // CR1 — at least one way to reach them
    public async Task Customer_without_email_and_phone_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var request = CustomerTestData.NewCustomer() with { Email = null, Phone = null, PreferredChannel = ContactChannel.Phone };

        var response = await admin.PostAsJsonAsync("/api/customers", request, Json.Options);

        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("email");
    }

    [Fact] // CR2
    public async Task Preferred_channel_must_be_reachable()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var request = CustomerTestData.NewCustomer() with { Phone = null, PreferredChannel = ContactChannel.WhatsApp };

        var response = await admin.PostAsJsonAsync("/api/customers", request, Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["preferredChannel"]
            .Should().ContainSingle().Which.Should().Contain("phone");
    }

    [Fact] // CR3 / CM2
    public async Task Duplicate_email_is_rejected_case_insensitively_but_keeping_your_own_is_fine()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@acme.test";
        var first = await admin.CreateCustomerAsync(CustomerTestData.NewCustomer(email));

        var duplicate = await admin.PostAsJsonAsync("/api/customers", CustomerTestData.NewCustomer(email.ToUpperInvariant()), Json.Options);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var keep = await admin.PutAsJsonAsync($"/api/customers/{first.Id}", CustomerTestData.NewCustomer(email, "Same email"), Json.Options);
        keep.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] // CM3
    public async Task List_supports_search_by_name_and_code_filters_and_paging()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var a = await admin.CreateCustomerAsync(CustomerTestData.NewCustomer(name: $"{tag} Alpha"));
        await admin.CreateCustomerAsync(CustomerTestData.NewCustomer(name: $"{tag} Beta"));
        await admin.CreateCustomerAsync(CustomerTestData.NewCustomer(name: $"{tag} Gamma") with { Type = CustomerType.Individual });

        var page = await admin.GetFromJsonAsync<PagedResult<CustomerListItemDto>>(
            $"/api/customers?search={tag}&type=Company&sortBy=name&sortDirection=asc&page=1&pageSize=1", Json.Options);
        page!.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle().Which.Name.Should().Be($"{tag} Alpha");

        var byCode = await admin.GetFromJsonAsync<PagedResult<CustomerListItemDto>>($"/api/customers?search={a.Code}", Json.Options);
        byCode!.Items.Should().Contain(c => c.Id == a.Id);
    }

    [Fact] // CM3 — last interaction shown in the list
    public async Task List_shows_last_interaction_date()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var at = DateTime.UtcNow.AddHours(-2);
        await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/interactions",
            new LogInteractionRequest(InteractionType.Call, InteractionDirection.Inbound, "Billing question", null, at), Json.Options);

        var page = await admin.GetFromJsonAsync<PagedResult<CustomerListItemDto>>($"/api/customers?search={customer.Code}", Json.Options);

        page!.Items.Single().LastInteractionAt.Should().BeCloseTo(at, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Enums_are_sent_as_strings()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();

        var json = await admin.GetStringAsync($"/api/customers/{customer.Id}");

        json.Should().Contain("\"type\":\"Company\"").And.Contain("\"preferredChannel\":\"Email\"");
    }

    [Fact] // CM4 / CR4 / CR5
    public async Task Contacts_can_be_managed_with_a_single_primary()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var url = $"/api/customers/{customer.Id}/contacts";

        await admin.PostAsJsonAsync(url, new ContactRequest("Omar", "CFO", "omar@acme.test", null, IsPrimary: true), Json.Options);
        var contacts = await (await admin.PostAsJsonAsync(url, new ContactRequest("Lina", "IT lead", null, "+966 50 000 0000", IsPrimary: true), Json.Options))
            .Content.ReadFromJsonAsync<List<CustomerContactDto>>(Json.Options);

        contacts.Should().HaveCount(2);
        contacts!.Where(c => c.IsPrimary).Should().ContainSingle().Which.Name.Should().Be("Lina");

        var omar = contacts.Single(c => c.Name == "Omar");
        var edited = await (await admin.PutAsJsonAsync($"{url}/{omar.Id}", new ContactRequest("Omar K.", "CEO", "omar@acme.test", null, false), Json.Options))
            .Content.ReadFromJsonAsync<List<CustomerContactDto>>(Json.Options);
        edited!.Should().Contain(c => c.Name == "Omar K." && c.JobTitle == "CEO");

        var afterDelete = await (await admin.DeleteAsync($"{url}/{omar.Id}")).Content.ReadFromJsonAsync<List<CustomerContactDto>>(Json.Options);
        afterDelete.Should().ContainSingle();

        var invalid = await admin.PostAsJsonAsync(url, new ContactRequest("No way to reach", null, null, null, false), Json.Options);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CM8
    public async Task Customer_endpoints_require_permissions()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var viewer = await ClientWithAsync(admin, Permissions.Customers.View);

        (await viewer.GetAsync($"/api/customers/{customer.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync("/api/customers", CustomerTestData.NewCustomer(), Json.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync($"/api/customers/{customer.Id}", CustomerTestData.NewCustomer(), Json.Options)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/customers/{customer.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/customers/{customer.Id}/notes", new NoteRequest("hi"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var nobody = await ClientWithAsync(admin);
        (await nobody.GetAsync("/api/customers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> ClientWithAsync(HttpClient admin, params string[] permissions)
    {
        var role = await admin.CreateRoleAsync(permissions);
        var user = await admin.CreateUserAsync(role.Id);
        return await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);
    }
}
