namespace Portal.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public abstract class PagedQuery
{
    public const int MaxPageSize = 100;

    private int _page = 1;
    private int _pageSize = 10;

    public int Page { get => _page; set => _page = Math.Max(1, value); }
    public int PageSize { get => _pageSize; set => _pageSize = Math.Clamp(value, 1, MaxPageSize); }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }

    public bool IsDescending => string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
}
