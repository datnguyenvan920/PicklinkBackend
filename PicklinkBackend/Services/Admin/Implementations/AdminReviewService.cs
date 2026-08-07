using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminReviewService : IAdminReviewService
{
    private static readonly string[] ModerationStatuses = ["Visible", "Hidden", "Flagged"];
    private readonly IAdminRepository _adminRepository;

    public AdminReviewService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
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

        var (items, totalCount) = await _adminRepository.GetAdminReviewListAsync(
            keyword, normalizedStatus, normalizedTargetType, score, page, pageSize, cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
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

        var review = await _adminRepository.GetRatingHistoryByIdAsync(ratingId, cancellationToken);
        if (review is null)
            return AdminReviewModerationResult.NotFound("Không tìm thấy đánh giá.");

        review.IsHidden = normalizedStatus == "Hidden";
        review.ModerationStatus = normalizedStatus;
        review.ModerationNote = string.IsNullOrWhiteSpace(request.ModerationNote)
            ? null
            : request.ModerationNote.Trim();
        review.ModeratedAt = DateTime.UtcNow;
        review.ModeratedByUserId = reviewerId;

        await _adminRepository.SaveChangesAsync(cancellationToken);

        return AdminReviewModerationResult.Success(Map(review));
    }

    public static AdminReviewResponse Map(RatingHistory review) => new()
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
    };

    private static string? NormalizeStatus(string? status) =>
        ModerationStatuses.FirstOrDefault(item =>
            item.Equals(status?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
