using Microsoft.AspNetCore.Mvc;
using Portal.API.Authorization;
using Portal.Application.Common.Models;
using Portal.Application.Features.Customers;
using Portal.Domain.Authorization;

namespace Portal.API.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(ICustomerService customers, ICustomerActivityService activity) : ControllerBase
{
    // ---------- Profile ----------

    [HttpGet]
    [HasPermission(Permissions.Customers.View)]
    public Task<PagedResult<CustomerListItemDto>> GetAll([FromQuery] CustomerListQuery query, CancellationToken ct) =>
        customers.GetPagedAsync(query, ct);

    [HttpGet("{id:int}")]
    [HasPermission(Permissions.Customers.View)]
    public Task<CustomerDto> GetById(int id, CancellationToken ct) => customers.GetByIdAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Customers.Create)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDto>> Create(CustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:int}")]
    [HasPermission(Permissions.Customers.Edit)]
    public Task<CustomerDto> Update(int id, CustomerRequest request, CancellationToken ct) =>
        customers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:int}")]
    [HasPermission(Permissions.Customers.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---------- Contacts (return the updated list) ----------

    [HttpPost("{id:int}/contacts")]
    [HasPermission(Permissions.Customers.Edit)]
    public Task<IReadOnlyList<CustomerContactDto>> AddContact(int id, ContactRequest request, CancellationToken ct) =>
        customers.AddContactAsync(id, request, ct);

    [HttpPut("{id:int}/contacts/{contactId:guid}")]
    [HasPermission(Permissions.Customers.Edit)]
    public Task<IReadOnlyList<CustomerContactDto>> UpdateContact(int id, Guid contactId, ContactRequest request, CancellationToken ct) =>
        customers.UpdateContactAsync(id, contactId, request, ct);

    [HttpDelete("{id:int}/contacts/{contactId:guid}")]
    [HasPermission(Permissions.Customers.Edit)]
    public Task<IReadOnlyList<CustomerContactDto>> DeleteContact(int id, Guid contactId, CancellationToken ct) =>
        customers.DeleteContactAsync(id, contactId, ct);

    // ---------- Interactions ----------

    [HttpGet("{id:int}/interactions")]
    [HasPermission(Permissions.Customers.View)]
    public Task<PagedResult<InteractionDto>> GetInteractions(int id, [FromQuery] InteractionQuery query, CancellationToken ct) =>
        activity.GetInteractionsAsync(id, query, ct);

    [HttpPost("{id:int}/interactions")]
    [HasPermission(Permissions.Customers.AddActivity)]
    [ProducesResponseType<InteractionDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InteractionDto>> LogInteraction(int id, LogInteractionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await activity.LogInteractionAsync(id, request, ct));

    // ---------- Notes ----------

    [HttpGet("{id:int}/notes")]
    [HasPermission(Permissions.Customers.View)]
    public Task<IReadOnlyList<NoteDto>> GetNotes(int id, CancellationToken ct) => activity.GetNotesAsync(id, ct);

    [HttpPost("{id:int}/notes")]
    [HasPermission(Permissions.Customers.AddActivity)]
    [ProducesResponseType<NoteDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<NoteDto>> AddNote(int id, NoteRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await activity.AddNoteAsync(id, request, ct));

    [HttpPut("{id:int}/notes/{noteId:guid}")]
    [HasPermission(Permissions.Customers.AddActivity)]
    public Task<NoteDto> UpdateNote(int id, Guid noteId, NoteRequest request, CancellationToken ct) =>
        activity.UpdateNoteAsync(id, noteId, request, ct);

    [HttpDelete("{id:int}/notes/{noteId:guid}")]
    [HasPermission(Permissions.Customers.AddActivity)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteNote(int id, Guid noteId, CancellationToken ct)
    {
        await activity.DeleteNoteAsync(id, noteId, ct);
        return NoContent();
    }

    // ---------- Attachments ----------

    [HttpGet("{id:int}/attachments")]
    [HasPermission(Permissions.Customers.View)]
    public Task<IReadOnlyList<AttachmentDto>> GetAttachments(int id, CancellationToken ct) => activity.GetAttachmentsAsync(id, ct);

    private const long UploadRequestLimit = AttachmentRules.MaxBytes + 1024 * 1024; // file + multipart overhead

    [HttpPost("{id:int}/attachments")]
    [HasPermission(Permissions.Customers.AddActivity)]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    [ProducesResponseType<AttachmentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AttachmentDto>> Upload(int id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await activity.UploadAttachmentAsync(id, new UploadedFile(stream, file.FileName, file.Length), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{id:int}/attachments/{attachmentId:guid}/download")]
    [HasPermission(Permissions.Customers.View)]
    public async Task<IActionResult> Download(int id, Guid attachmentId, CancellationToken ct)
    {
        var download = await activity.DownloadAttachmentAsync(id, attachmentId, ct);
        // FileStreamResult disposes the stream; the file name makes it Content-Disposition: attachment.
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:guid}")]
    [HasPermission(Permissions.Customers.AddActivity)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAttachment(int id, Guid attachmentId, CancellationToken ct)
    {
        await activity.DeleteAttachmentAsync(id, attachmentId, ct);
        return NoContent();
    }
}
