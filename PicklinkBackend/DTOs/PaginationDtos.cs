using Microsoft.EntityFrameworkCore;

namespace PicklinkBackend.DTOs;

public class PaginatedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public static class Pagination
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public static int NormalizePage(int page) => Math.Max(page, 1);
    public static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, MaxPageSize);

    public static PaginatedResponse<T> Create<T>(
        IEnumerable<T> source,
        int totalCount,
        int page,
        int pageSize)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        return new PaginatedResponse<T>
        {
            Items = source.ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public static async Task<PaginatedResponse<T>> CreateAsync<T>(
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Create(items, totalCount, page, pageSize);
    }
}
