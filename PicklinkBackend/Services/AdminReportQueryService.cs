using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class AdminReportQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminReportQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaginatedResponse<AdminReportResponse>> ListAsync(
        string? search,
        string? status,
        string? targetType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedTargetType = Normalize(targetType);

        var query = _dbContext.CommunityReports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(report =>
                report.TargetLabel.Contains(keyword)
                || report.Reason.Contains(keyword)
                || (report.Description != null && report.Description.Contains(keyword))
                || report.ReporterUser.Username.Contains(keyword)
                || report.ReporterUser.Email.Contains(keyword));
        }
        if (normalizedStatus is not null)
            query = query.Where(report => report.Status == normalizedStatus);
        if (normalizedTargetType is not null)
            query = query.Where(report => report.TargetType == normalizedTargetType);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(report => report.Status == "Open")
            .ThenByDescending(report => report.Priority == "High")
            .ThenByDescending(report => report.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(report => new AdminReportResponse
            {
                CommunityReportId = report.CommunityReportId,
                ReporterUserId = report.ReporterUserId,
                ReporterName = report.ReporterUser.Username,
                ReporterEmail = report.ReporterUser.Email,
                TargetType = report.TargetType,
                TargetId = report.TargetId,
                TargetLabel = report.TargetLabel,
                Reason = report.Reason,
                Description = report.Description,
                Status = report.Status,
                Priority = report.Priority,
                CreatedAt = report.CreatedAt,
                ReviewedAt = report.ReviewedAt,
                ReviewedByName = report.ReviewedByUser != null ? report.ReviewedByUser.Username : null,
                ResolutionNote = report.ResolutionNote
            })
            .ToListAsync(cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    internal static AdminReportResponse Map(CommunityReport report) => new()
    {
        CommunityReportId = report.CommunityReportId,
        ReporterUserId = report.ReporterUserId,
        ReporterName = report.ReporterUser.Username,
        ReporterEmail = report.ReporterUser.Email,
        TargetType = report.TargetType,
        TargetId = report.TargetId,
        TargetLabel = report.TargetLabel,
        Reason = report.Reason,
        Description = report.Description,
        Status = report.Status,
        Priority = report.Priority,
        CreatedAt = report.CreatedAt,
        ReviewedAt = report.ReviewedAt,
        ReviewedByName = report.ReviewedByUser?.Username,
        ResolutionNote = report.ResolutionNote
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}