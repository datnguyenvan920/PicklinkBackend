using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services;

public sealed class AdminReviewModerationService
{
    private static readonly string[] ModerationStatuses = ["Visible", "Hidden", "Flagged"];
    private readonly ApplicationDbContext _dbContext;

    public AdminReviewModerationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string? Validate(AdminReviewModerationRequest request) =>
        NormalizeStatus(request.ModerationStatus) is null
            ? "Trạng thái kiểm duyệt đánh giá không hợp lệ."
            : null;

    public async Task<AdminReviewModerationResult> ModerateAsync(
        int ratingId,
        AdminReviewModerationRequest request,
        int reviewerId,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = NormalizeStatus(request.ModerationStatus);
        if (normalizedStatus is null)
        {
            return AdminReviewModerationResult.BadRequest(
                "Trạng thái kiểm duyệt đánh giá không hợp lệ.");
        }

        var review = await _dbContext.RatingHistories
            .Include(item => item.User)
            .Include(item => item.ModeratedByUser)
            .SingleOrDefaultAsync(item => item.RatingId == ratingId, cancellationToken);
        if (review is null)
            return AdminReviewModerationResult.NotFound("Không tìm thấy đánh giá.");

        review.IsHidden = request.IsHidden;
        review.ModerationStatus = normalizedStatus;
        review.ModerationNote = string.IsNullOrWhiteSpace(request.ModerationNote)
            ? null
            : request.ModerationNote.Trim();
        review.ModeratedAt = DateTime.UtcNow;
        review.ModeratedByUserId = reviewerId;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AdminReviewModerationResult.Success(AdminReviewQueryService.Map(review));
    }

    private static string? NormalizeStatus(string? status) =>
        ModerationStatuses.FirstOrDefault(item =>
            item.Equals(status?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed record AdminReviewModerationResult(
    AdminReviewModerationResultStatus Status,
    AdminReviewResponse? Review = null,
    string? ErrorMessage = null)
{
    public static AdminReviewModerationResult Success(AdminReviewResponse review) =>
        new(AdminReviewModerationResultStatus.Success, review, ErrorMessage: null);

    public static AdminReviewModerationResult BadRequest(string errorMessage) =>
        new(AdminReviewModerationResultStatus.BadRequest, Review: null, ErrorMessage: errorMessage);

    public static AdminReviewModerationResult NotFound(string errorMessage) =>
        new(AdminReviewModerationResultStatus.NotFound, Review: null, ErrorMessage: errorMessage);
}

public enum AdminReviewModerationResultStatus
{
    Success,
    BadRequest,
    NotFound
}
