using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reviews")]
public class AdminReviewsController : ControllerBase
{
    private static readonly string[] ModerationStatuses = ["Visible", "Hidden", "Flagged"];
    private readonly ApplicationDbContext _dbContext;

    public AdminReviewsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminReviewResponse>>> GetReviews(
        string? search,
        string? moderationStatus,
        string? targetType,
        int? score,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
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
                ReviewerName = review.IsAnonymous ? "Ẩn danh" : review.User.Username,
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

        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }

    [HttpPost("{ratingId:int}/moderate")]
    public async Task<ActionResult<AdminReviewResponse>> ModerateReview(
        int ratingId,
        AdminReviewModerationRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = ModerationStatuses.FirstOrDefault(status =>
            status.Equals(request.ModerationStatus?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedStatus is null)
            return BadRequest(new { message = "Trạng thái kiểm duyệt đánh giá không hợp lệ." });

        var reviewerId = CurrentUserId();
        if (reviewerId is null) return Unauthorized();

        var review = await _dbContext.RatingHistories
            .Include(item => item.User)
            .Include(item => item.ModeratedByUser)
            .SingleOrDefaultAsync(item => item.RatingId == ratingId, cancellationToken);
        if (review is null) return NotFound(new { message = "Không tìm thấy đánh giá." });

        review.IsHidden = request.IsHidden;
        review.ModerationStatus = normalizedStatus;
        review.ModerationNote = string.IsNullOrWhiteSpace(request.ModerationNote)
            ? null
            : request.ModerationNote.Trim();
        review.ModeratedAt = DateTime.UtcNow;
        review.ModeratedByUserId = reviewerId.Value;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new AdminReviewResponse
        {
            RatingId = review.RatingId,
            ReviewerUserId = review.UserId,
            ReviewerName = review.IsAnonymous ? "Ẩn danh" : review.User.Username,
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
        });
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}

public sealed class AdminReviewModerationRequest
{
    public bool IsHidden { get; set; }

    [Required]
    public string ModerationStatus { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? ModerationNote { get; set; }
}

public sealed class AdminReviewResponse
{
    public int RatingId { get; set; }
    public int ReviewerUserId { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public string? ReviewerEmail { get; set; }
    public int? BookingId { get; set; }
    public int TargetId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public int Score { get; set; }
    public string? Comment { get; set; }
    public string? Tags { get; set; }
    public bool IsAnonymous { get; set; }
    public bool IsHidden { get; set; }
    public string ModerationStatus { get; set; } = string.Empty;
    public string? ModerationNote { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public string? ModeratedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}
