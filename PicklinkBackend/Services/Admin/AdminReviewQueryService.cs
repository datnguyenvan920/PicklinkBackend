using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Admin;

public sealed class AdminReviewQueryService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminReviewQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaginatedResponse<AdminReviewResponse>> ListAsync(
        string? search,
        string? moderationStatus,
        string? targetType,
        int? score,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(moderationStatus);
        var normalizedTargetType = Normalize(targetType);

        var query = _dbContext.RatingHistories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(review =>
                (review.Comment != null && review.Comment.Contains(keyword))
                || (review.Tags != null && review.Tags.Contains(keyword))
                || review.User.Username.Contains(keyword)
                || review.User.Email.Contains(keyword));
        }

        if (normalizedStatus is not null)
            query = query.Where(review => review.ModerationStatus == normalizedStatus);
        if (normalizedTargetType is not null)
            query = query.Where(review => review.TargetType == normalizedTargetType);
        if (score is >= 1 and <= 5)
            query = query.Where(review => review.Score == score.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(review => review.ModerationStatus == "Flagged")
            .ThenByDescending(review => review.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(review => new AdminReviewResponse
            {
                RatingId = review.RatingId,
                ReviewerUserId = review.UserId,
                ReviewerName = review.IsAnonymous ? "Ã¡ÂºÂ¨n danh" : review.User.Username,
                ReviewerEmail = review.IsAnonymous ? null : review.User.Email,
                BookingId = review.BookingId,
                TargetId = review.TargetId,
                TargetType = review.TargetType,
                Score = review.Score,
                Comment = review.Comment,
                Tags = review.Tags,
                IsAnonymous = review.IsAnonymous,
                IsHidden = review.IsHidden,
                ModerationStatus = review.ModerationStatus,
                ModerationNote = review.ModerationNote,
                ModeratedAt = review.ModeratedAt,
                ModeratedByName = review.ModeratedByUser != null ? review.ModeratedByUser.Username : null,
                CreatedAt = review.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    internal static AdminReviewResponse Map(RatingHistory review) => new()
    {
        RatingId = review.RatingId,
        ReviewerUserId = review.UserId,
        ReviewerName = review.IsAnonymous ? "Ã¡ÂºÂ¨n danh" : review.User.Username,
        ReviewerEmail = review.IsAnonymous ? null : review.User.Email,
        BookingId = review.BookingId,
        TargetId = review.TargetId,
        TargetType = review.TargetType,
        Score = review.Score,
        Comment = review.Comment,
        Tags = review.Tags,
        IsAnonymous = review.IsAnonymous,
        IsHidden = review.IsHidden,
        ModerationStatus = review.ModerationStatus,
        ModerationNote = review.ModerationNote,
        ModeratedAt = review.ModeratedAt,
        ModeratedByName = review.ModeratedByUser?.Username,
        CreatedAt = review.CreatedAt
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
