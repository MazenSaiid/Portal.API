using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Portal.Application.Common.Models;
using Portal.Application.Features.Customers;
using Portal.Domain.Authorization;
using Portal.Domain.Entities.Customers;
using Portal.Tests.Infrastructure;

namespace Portal.Tests.Integration;

public sealed class CustomerActivityTests(PortalApiFactory factory) : IClassFixture<PortalApiFactory>
{
    // ---------- Interactions ----------

    [Fact] // CM5
    public async Task Interactions_are_logged_and_listed_newest_first()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var url = $"/api/customers/{customer.Id}/interactions";

        await admin.PostAsJsonAsync(url, new LogInteractionRequest(InteractionType.Email, InteractionDirection.Inbound, "Older", null, DateTime.UtcNow.AddDays(-1)), Json.Options);
        var logged = await admin.PostAsJsonAsync(url, new LogInteractionRequest(InteractionType.Call, InteractionDirection.Outbound, "Newer", "Called back", null), Json.Options);
        logged.StatusCode.Should().Be(HttpStatusCode.Created);
        (await logged.Content.ReadFromJsonAsync<InteractionDto>(Json.Options))!.CreatedByName.Should().Be("System Administrator");

        var list = await admin.GetFromJsonAsync<PagedResult<InteractionDto>>(url, Json.Options);
        list!.Items.Select(i => i.Subject).Should().Equal("Newer", "Older");
        list.Items[0].Type.Should().Be(InteractionType.Call);
    }

    [Fact] // CR6
    public async Task Interaction_in_the_future_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();

        var response = await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/interactions",
            new LogInteractionRequest(InteractionType.Call, InteractionDirection.Inbound, "Tomorrow", null, DateTime.UtcNow.AddDays(1)), Json.Options);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("occurredAt");
    }

    [Fact] // C5 — immutable history
    public async Task Interactions_cannot_be_edited_or_deleted()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var created = await (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/interactions",
            new LogInteractionRequest(InteractionType.Chat, InteractionDirection.Inbound, "Hi", null, null), Json.Options))
            .Content.ReadFromJsonAsync<InteractionDto>(Json.Options);

        var url = $"/api/customers/{customer.Id}/interactions/{created!.Id}";
        (await admin.DeleteAsync(url)).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await admin.PutAsJsonAsync(url, new { subject = "changed" })).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    // ---------- Notes ----------

    [Fact] // CM6 / CR7
    public async Task Only_the_author_or_an_editor_can_change_a_note()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var url = $"/api/customers/{customer.Id}/notes";
        var agentA = await ClientWithAsync(admin, Permissions.Customers.View, Permissions.Customers.AddActivity);
        var agentB = await ClientWithAsync(admin, Permissions.Customers.View, Permissions.Customers.AddActivity);

        var note = await (await agentA.PostAsJsonAsync(url, new NoteRequest("VIP — always escalate"))).Content.ReadFromJsonAsync<NoteDto>();
        note!.CanManage.Should().BeTrue();

        // Another agent sees it but may not change it.
        (await agentB.GetFromJsonAsync<List<NoteDto>>(url))!.Single().CanManage.Should().BeFalse();
        (await agentB.PutAsJsonAsync($"{url}/{note.Id}", new NoteRequest("hacked"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agentB.DeleteAsync($"{url}/{note.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The author can edit; an editor (admin has Customers.Edit) can delete.
        var edited = await (await agentA.PutAsJsonAsync($"{url}/{note.Id}", new NoteRequest("VIP — escalate to supervisor")))
            .Content.ReadFromJsonAsync<NoteDto>();
        edited!.Content.Should().Be("VIP — escalate to supervisor");
        edited.UpdatedAt.Should().NotBeNull();
        (await admin.DeleteAsync($"{url}/{note.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<NoteDto>>(url)).Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_note_is_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();

        (await admin.PostAsJsonAsync($"/api/customers/{customer.Id}/notes", new NoteRequest("  ")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Attachments ----------

    [Fact] // CM7
    public async Task Upload_download_and_delete_an_attachment()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var url = $"/api/customers/{customer.Id}/attachments";
        var bytes = "%PDF-1.4 test contract"u8.ToArray();

        var upload = await admin.PostAsync(url, FileContent(bytes, "..\\..\\contract 2026.pdf", "text/html"));
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentDto>())!;
        attachment.FileName.Should().Be("contract 2026.pdf", "path segments are stripped from the name");
        attachment.ContentType.Should().Be("application/pdf", "the type comes from the extension, not the client");
        attachment.SizeBytes.Should().Be(bytes.Length);

        var download = await admin.GetAsync($"{url}/{attachment.Id}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        (await admin.DeleteAsync($"{url}/{attachment.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<List<AttachmentDto>>(url)).Should().BeEmpty();
        Directory.EnumerateFiles(factory.StorageRoot).Should().NotContain(f => f.EndsWith(".pdf") && File.ReadAllBytes(f).SequenceEqual(bytes));
    }

    [Theory] // CR8
    [InlineData("malware.exe", 10)]
    [InlineData("page.html", 10)]
    [InlineData("empty.pdf", 0)]
    public async Task Disallowed_or_empty_files_are_rejected(string fileName, int size)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();

        var response = await admin.PostAsync($"/api/customers/{customer.Id}/attachments", FileContent(new byte[size], fileName));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("file");
    }

    [Fact] // CR8
    public async Task Files_over_10_MB_are_rejected()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();

        var response = await admin.PostAsync($"/api/customers/{customer.Id}/attachments",
            FileContent(new byte[AttachmentRules.MaxBytes + 1], "big.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CR7 for files
    public async Task Another_agent_cannot_delete_someone_elses_file()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var url = $"/api/customers/{customer.Id}/attachments";
        var uploaded = await (await admin.PostAsync(url, FileContent("a,b"u8.ToArray(), "data.csv"))).Content.ReadFromJsonAsync<AttachmentDto>();
        var agent = await ClientWithAsync(admin, Permissions.Customers.View, Permissions.Customers.AddActivity);

        (await agent.DeleteAsync($"{url}/{uploaded!.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync($"{url}/{uploaded.Id}/download")).StatusCode.Should().Be(HttpStatusCode.OK, "viewing is allowed");
    }

    [Fact] // C8
    public async Task Deleting_a_customer_removes_its_files_from_storage()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var customer = await admin.CreateCustomerAsync();
        var marker = Guid.NewGuid().ToByteArray();
        (await admin.PostAsync($"/api/customers/{customer.Id}/attachments", FileContent(marker, "note.txt"))).EnsureSuccessStatusCode();
        StoredFilesWith(marker).Should().ContainSingle();

        (await admin.DeleteAsync($"/api/customers/{customer.Id}")).EnsureSuccessStatusCode();

        StoredFilesWith(marker).Should().BeEmpty();
    }

    private IEnumerable<string> StoredFilesWith(byte[] content) =>
        Directory.EnumerateFiles(factory.StorageRoot).Where(f => File.ReadAllBytes(f).SequenceEqual(content));

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType = "application/octet-stream")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private async Task<HttpClient> ClientWithAsync(HttpClient admin, params string[] permissions)
    {
        var role = await admin.CreateRoleAsync(permissions);
        var user = await admin.CreateUserAsync(role.Id);
        return await factory.CreateAuthenticatedClientAsync(user.Email, ApiClientExtensions.DefaultPassword);
    }
}
