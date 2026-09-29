using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Common.Exceptions;
using Portal.Application.Common.Interfaces;
using Portal.Domain.Entities.Tickets;

namespace Portal.Application.Features.Tickets;

public interface ITicketCategoryService
{
    Task<IReadOnlyList<TicketCategoryDto>> GetAllAsync(bool activeOnly, CancellationToken ct = default);
    Task<TicketCategoryDto> CreateAsync(TicketCategoryRequest request, CancellationToken ct = default);
    Task<TicketCategoryDto> UpdateAsync(int id, TicketCategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class TicketCategoryService(IApplicationDbContext db, IValidator<TicketCategoryRequest> validator) : ITicketCategoryService
{
    public async Task<IReadOnlyList<TicketCategoryDto>> GetAllAsync(bool activeOnly, CancellationToken ct = default) =>
        await db.TicketCategories.AsNoTracking()
            .Where(c => !activeOnly || c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new TicketCategoryDto(c.Id, c.Name, c.Description, c.IsActive, db.Tickets.Count(t => t.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<TicketCategoryDto> CreateAsync(TicketCategoryRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var category = new TicketCategory();
        await ApplyAsync(category, request, ct);
        db.TicketCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return new TicketCategoryDto(category.Id, category.Name, category.Description, category.IsActive, 0);
    }

    public async Task<TicketCategoryDto> UpdateAsync(int id, TicketCategoryRequest request, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var category = await db.TicketCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category", id);
        await ApplyAsync(category, request, ct);
        await db.SaveChangesAsync(ct);
        var count = await db.Tickets.CountAsync(t => t.CategoryId == id, ct);
        return new TicketCategoryDto(category.Id, category.Name, category.Description, category.IsActive, count);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var category = await db.TicketCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category", id);
        var used = await db.Tickets.CountAsync(t => t.CategoryId == id, ct);
        if (used > 0) // T3
            throw new ConflictException($"\"{category.Name}\" is used by {used} ticket(s). Deactivate it instead.");
        db.TicketCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(TicketCategory category, TicketCategoryRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        var upper = name.ToUpper();
        if (await db.TicketCategories.AnyAsync(c => c.Name.ToUpper() == upper && c.Id != category.Id, ct))
            throw new ConflictException($"A category named \"{name}\" already exists.");

        category.Name = name;
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.IsActive = request.IsActive;
    }
}
