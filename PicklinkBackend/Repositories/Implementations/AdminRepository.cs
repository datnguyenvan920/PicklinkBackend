using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Repositories.Implementations;

public class AdminRepository : IAdminRepository
{
    private readonly ApplicationDbContext _dbContext;

    public AdminRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(List<AdminUserSummaryResponse> Items, int TotalCount)> GetAdminUserListAsync(
        string? keyword,
        string? normalizedRole,
        bool lockedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(user =>
                user.Username.Contains(keyword)
                || user.Email.Contains(keyword)
                || (user.City != null && user.City.Contains(keyword))
                || (user.Commune != null && user.Commune.Contains(keyword)));
        }

        if (normalizedRole is not null)
            query = query.Where(user => user.UserType == normalizedRole);

        if (lockedOnly)
            query = query.Where(user => user.IsLocked);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(user => user.IsLocked)
            .ThenBy(user => user.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdminUserSummaryResponse
            {
                UserId = user.UserId,
                Name = user.Username,
                Email = user.Email,
                Role = user.UserType,
                RoleLabel = RoleLabel(user.UserType),
                IsLocked = user.IsLocked,
                LockReason = user.LockReason,
                LockedAt = user.LockedAt,
                LockedByName = user.LockedByUser != null ? user.LockedByUser.Username : null,
                UnlockedAt = user.UnlockedAt,
                UnlockedByName = user.UnlockedByUser != null ? user.UnlockedByUser.Username : null,
                City = user.City,
                Commune = user.Commune,
                AvatarUrl = user.ProfileImageUrl,
                JoinedClubCount = user.GroupMembers.Count(member => member.Status == "Accepted"),
                OwnedVenueCount = user.VenueOwners.SelectMany(owner => owner.Venues).Count(),
                BookingCount = user.Players.SelectMany(player => player.Bookings).Count()
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<User?> GetUserForLockByIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Users
            .Include(user => user.GroupMembers)
            .Include(user => user.VenueOwners).ThenInclude(owner => owner.Venues)
            .Include(user => user.Players).ThenInclude(player => player.Bookings)
            .Include(user => user.LockedByUser)
            .Include(user => user.UnlockedByUser)
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);
    }

    public async Task<(List<AdminVenueSummaryResponse> Items, int TotalCount)> GetAdminVenueListAsync(
        string? keyword,
        string? normalizedStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Venues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(venue =>
                venue.VenueName.Contains(keyword)
                || venue.Address.Contains(keyword)
                || venue.Owner.User.Username.Contains(keyword)
                || venue.Owner.User.Email.Contains(keyword));
        }

        if (normalizedStatus is not null)
            query = query.Where(venue => venue.ApprovalStatus == normalizedStatus);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(venue => venue.ApprovalStatus == "Pending")
            .ThenByDescending(venue => venue.VenueAuditLogs
                .Where(log => log.Action == "OwnerSubmittedForApproval")
                .Select(log => (DateTime?)log.Timestamp)
                .Max())
            .ThenBy(venue => venue.VenueName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(venue => new AdminVenueSummaryResponse
            {
                VenueId = venue.VenueId,
                VenueName = venue.VenueName,
                Address = venue.Address,
                OwnerUserId = venue.Owner.UserId,
                OwnerName = venue.Owner.User.Username,
                OwnerEmail = venue.Owner.User.Email,
                PhoneNumber = venue.PhoneNumber,
                OverallRating = venue.OverallRating,
                IsOpen = venue.IsOpen,
                ApprovalStatus = venue.ApprovalStatus,
                RejectionReason = venue.RejectionReason,
                CourtCount = venue.Courts.Count,
                PrimaryImageUrl = venue.VenueImages
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                SubmittedAt = venue.VenueAuditLogs
                    .Where(log => log.Action == "OwnerSubmittedForApproval")
                    .Select(log => (DateTime?)log.Timestamp)
                    .Max()
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Venue?> GetAdminVenueDetailAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Venues
            .Include(venue => venue.Owner).ThenInclude(owner => owner.User)
            .Include(venue => venue.Amenities)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .Include(venue => venue.Courts)
            .Include(venue => venue.VenueAuditLogs).ThenInclude(log => log.Actor)
            .AsSplitQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(venue => venue.VenueId == venueId, cancellationToken);
    }

    public Task<Venue?> GetVenueForApprovalByIdAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Venues
            .Include(venue => venue.Owner).ThenInclude(owner => owner.User)
            .Include(venue => venue.Amenities)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .Include(venue => venue.Courts)
            .Include(venue => venue.VenueAuditLogs).ThenInclude(log => log.Actor)
            .AsSplitQuery()
            .SingleOrDefaultAsync(venue => venue.VenueId == venueId, cancellationToken);
    }

    public async Task<(List<AdminBookingSummaryResponse> Items, int TotalCount)> GetAdminBookingListAsync(
        string? keyword,
        string? normalizedStatus,
        string? normalizedPaymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(booking =>
                (booking.BookingCode != null && booking.BookingCode.Contains(keyword))
                || booking.Court.Venue.VenueName.Contains(keyword)
                || booking.Court.Venue.Owner.User.Username.Contains(keyword)
                || booking.Court.Venue.Owner.User.Email.Contains(keyword)
                || (booking.Player != null && booking.Player.User.Username.Contains(keyword))
                || (booking.Player != null && booking.Player.User.Email.Contains(keyword)));
        }

        if (normalizedStatus is not null)
            query = query.Where(booking => booking.Status == normalizedStatus);

        if (normalizedPaymentStatus is not null)
        {
            query = normalizedPaymentStatus.Equals("NoPayment", StringComparison.OrdinalIgnoreCase)
                ? query.Where(booking => !booking.Payments.Any())
                : query.Where(booking => booking.Payments.Any(payment => payment.Status == normalizedPaymentStatus));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(booking => booking.StartTime)
            .ThenByDescending(booking => booking.BookingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(booking => new AdminBookingSummaryResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Status = booking.Status,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                CreatedAt = booking.CreatedAt,
                TotalAmount = booking.TotalAmount,
                CourtAmount = booking.CourtAmount,
                VenueId = booking.Court.VenueId,
                VenueName = booking.Court.Venue.VenueName,
                CourtId = booking.CourtId,
                CourtNumber = booking.Court.CourtNumber,
                OwnerName = booking.Court.Venue.Owner.User.Username,
                OwnerEmail = booking.Court.Venue.Owner.User.Email,
                PlayerName = booking.Player != null ? booking.Player.User.Username : "Owner tạo lịch",
                PlayerEmail = booking.Player != null ? booking.Player.User.Email : null,
                PaymentStatus = booking.Payments
                    .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.Status)
                    .FirstOrDefault() ?? "NoPayment",
                PaymentMethod = booking.Payments
                    .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.PaymentMethod)
                    .FirstOrDefault(),
                PaymentSubmittedAt = booking.Payments
                    .OrderByDescending(payment => payment.SubmittedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.SubmittedAt)
                    .FirstOrDefault(),
                PaymentVerifiedAt = booking.Payments
                    .OrderByDescending(payment => payment.VerifiedAt ?? payment.PaidAt ?? DateTime.MinValue)
                    .Select(payment => payment.VerifiedAt)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<AdminDashboardResponse> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var localNow = VietnamTime.Now;
        var localTodayStart = localNow.Date;
        var localMonthStart = new DateTime(localNow.Year, localNow.Month, 1);
        var todayStart = VietnamTime.ToUtc(localTodayStart);
        var tomorrowStart = VietnamTime.ToUtc(localTodayStart.AddDays(1));
        var monthStart = VietnamTime.ToUtc(localMonthStart);
        var nextMonthStart = VietnamTime.ToUtc(localMonthStart.AddMonths(1));
        var expiringThreshold = now.AddDays(7);

        var totalUsers = await _dbContext.Users.AsNoTracking().CountAsync(cancellationToken);
        var lockedUserCount = await _dbContext.Users.AsNoTracking()
            .CountAsync(user => user.IsLocked, cancellationToken);
        var activeVenueCount = await _dbContext.Venues.AsNoTracking()
            .CountAsync(venue => venue.ApprovalStatus == "Approved" && venue.IsOpen, cancellationToken);
        var pendingVenueCount = await _dbContext.Venues.AsNoTracking()
            .CountAsync(venue => venue.ApprovalStatus == "Pending", cancellationToken);
        var totalCourtCount = await _dbContext.Courts.AsNoTracking()
            .CountAsync(court => court.AvailabilityStatus != "Inactive", cancellationToken);
        var todayBookingCount = await _dbContext.Bookings.AsNoTracking()
            .CountAsync(booking => booking.CreatedAt >= todayStart && booking.CreatedAt < tomorrowStart, cancellationToken);
        var todayBookingRevenue = await _dbContext.Payments.AsNoTracking()
            .Where(payment => payment.Status == "Verified"
                && payment.PaidAt != null
                && payment.PaidAt >= todayStart
                && payment.PaidAt < tomorrowStart)
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0;
        var pendingBookingPaymentCount = await _dbContext.Payments.AsNoTracking()
            .CountAsync(payment => payment.Status == "WaitingForConfirmation", cancellationToken);
        var pendingListingPaymentCount = await _dbContext.VenueListingPayments.AsNoTracking()
            .CountAsync(payment => payment.Status == "PendingReview", cancellationToken);
        var listingRevenueThisMonth = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "Confirmed"
                && payment.ReviewedAt != null
                && payment.ReviewedAt >= monthStart
                && payment.ReviewedAt < nextMonthStart)
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0;

        var latestListingExpirations = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "Confirmed" && payment.PaidUntil != null)
            .GroupBy(payment => payment.VenueId)
            .Select(group => new
            {
                VenueId = group.Key,
                PaidUntil = group.Max(payment => payment.PaidUntil)
            })
            .ToListAsync(cancellationToken);

        var expiringIds = latestListingExpirations
            .Where(item => item.PaidUntil >= now && item.PaidUntil <= expiringThreshold)
            .Select(item => item.VenueId)
            .ToList();
        var expiredIds = latestListingExpirations
            .Where(item => item.PaidUntil < now)
            .Select(item => item.VenueId)
            .ToList();

        var expiringListingCount = expiringIds.Count;
        var expiredListingCount = expiredIds.Count;
        var paidUntilByVenue = latestListingExpirations.ToDictionary(item => item.VenueId, item => item.PaidUntil);

        var expiringListings = new List<AdminDashboardExpiringListingResponse>();
        if (expiringIds.Count > 0)
        {
            expiringListings = await _dbContext.Venues.AsNoTracking()
                .Where(venue => expiringIds.Contains(venue.VenueId))
                .OrderBy(venue => venue.VenueName)
                .Take(8)
                .Select(venue => new AdminDashboardExpiringListingResponse
                {
                    VenueId = venue.VenueId,
                    VenueName = venue.VenueName,
                    OwnerName = venue.Owner.User.Username,
                    OwnerEmail = venue.Owner.User.Email,
                    CourtCount = venue.Courts.Count(court => court.AvailabilityStatus != "Inactive"),
                })
                .ToListAsync(cancellationToken);

            foreach (var listing in expiringListings)
            {
                listing.PaidUntil = paidUntilByVenue[listing.VenueId];
            }
        }

        var actionItems = await BuildActionItems(now, cancellationToken);
        actionItems.AddRange(expiringListings.Take(3).Select(item => new AdminDashboardActionItemResponse
        {
            Type = "ListingExpiring",
            Title = item.VenueName,
            Description = $"{item.OwnerName} - hết hạn phí lên sàn ngày {item.PaidUntil:dd/MM/yyyy}.",
            Status = "Sắp hết hạn",
            Tone = "warning",
            LinkTo = "/admin/transactions",
            CreatedAt = item.PaidUntil
        }));

        return new AdminDashboardResponse
        {
            TotalUsers = totalUsers,
            LockedUserCount = lockedUserCount,
            ActiveVenueCount = activeVenueCount,
            PendingVenueCount = pendingVenueCount,
            TotalCourtCount = totalCourtCount,
            TodayBookingCount = todayBookingCount,
            TodayBookingRevenue = todayBookingRevenue,
            PendingBookingPaymentCount = pendingBookingPaymentCount,
            PendingListingPaymentCount = pendingListingPaymentCount,
            ListingRevenueThisMonth = listingRevenueThisMonth,
            ExpiringListingCount = expiringListingCount,
            ExpiredListingCount = expiredListingCount,
            ActionItems = actionItems
                .OrderByDescending(item => item.Tone == "danger")
                .ThenByDescending(item => item.Tone == "warning")
                .ThenByDescending(item => item.CreatedAt)
                .Take(12)
                .ToList(),
            ExpiringListings = expiringListings
        };
    }

    public async Task<(List<AdminListingFeePaymentResponse> Items, int TotalCount)> GetAdminListingFeePaymentListAsync(
        string? normalizedStatus,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.VenueListingPayments.AsNoTracking();
        if (normalizedStatus is not null)
            query = query.Where(payment => payment.Status == normalizedStatus);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(payment =>
                payment.Venue.VenueName.Contains(keyword)
                || payment.Venue.Owner.User.Username.Contains(keyword)
                || payment.Venue.Owner.User.Email.Contains(keyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(payment => payment.Status == "PendingReview")
            .ThenByDescending(payment => payment.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(payment => new AdminListingFeePaymentResponse
            {
                VenueListingPaymentId = payment.VenueListingPaymentId,
                VenueId = payment.VenueId,
                VenueName = payment.Venue.VenueName,
                OwnerName = payment.Venue.Owner.User.Username,
                OwnerEmail = payment.Venue.Owner.User.Email,
                Months = payment.Months,
                ActiveCourtCount = payment.ActiveCourtCount,
                PricePerCourtPerMonth = payment.PricePerCourtPerMonth,
                Amount = payment.Amount,
                Status = payment.Status,
                ReceiptImageUrl = payment.ReceiptImageUrl,
                RejectionReason = payment.RejectionReason,
                SubmittedAt = payment.SubmittedAt,
                ReviewedAt = payment.ReviewedAt,
                PaidFrom = payment.PaidFrom,
                PaidUntil = payment.PaidUntil
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<VenueListingPayment?> GetVenueListingPaymentByIdAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        return _dbContext.VenueListingPayments
            .Include(payment => payment.Venue).ThenInclude(venue => venue.Owner).ThenInclude(owner => owner.User)
            .SingleOrDefaultAsync(payment => payment.VenueListingPaymentId == paymentId, cancellationToken);
    }

    public Task<DateTime?> GetLatestPaidUntilByVenueIdAsync(int venueId, CancellationToken cancellationToken = default)
    {
        return _dbContext.VenueListingPayments.AsNoTracking()
            .Where(item => item.VenueId == venueId && item.Status == "Confirmed" && item.PaidUntil != null)
            .MaxAsync(item => (DateTime?)item.PaidUntil, cancellationToken);
    }

    public Task<ListingFeeSetting?> GetLatestListingFeeSettingAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.ListingFeeSettings.AsNoTracking()
            .OrderByDescending(setting => setting.UpdatedAt)
            .ThenByDescending(setting => setting.ListingFeeSettingId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddListingFeeSettingAsync(ListingFeeSetting setting, CancellationToken cancellationToken = default)
    {
        await _dbContext.ListingFeeSettings.AddAsync(setting, cancellationToken);
    }

    public Task<Dictionary<string, PlatformSetting>> GetPlatformSettingsAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.PlatformSettings.AsNoTracking()
            .ToDictionaryAsync(setting => setting.SettingKey, StringComparer.OrdinalIgnoreCase, cancellationToken);
    }

    public Task<PlatformSetting?> GetPlatformSettingByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return _dbContext.PlatformSettings
            .SingleOrDefaultAsync(item => item.SettingKey == key, cancellationToken);
    }

    public async Task AddPlatformSettingAsync(PlatformSetting setting, CancellationToken cancellationToken = default)
    {
        await _dbContext.PlatformSettings.AddAsync(setting, cancellationToken);
    }

    public async Task<(List<AdminReportResponse> Items, int TotalCount)> GetAdminReportListAsync(
        string? keyword,
        string? normalizedStatus,
        string? normalizedTargetType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
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

        return (items, totalCount);
    }

    public Task<CommunityReport?> GetCommunityReportByIdAsync(int reportId, CancellationToken cancellationToken = default)
    {
        return _dbContext.CommunityReports
            .Include(item => item.ReporterUser)
            .Include(item => item.ReviewedByUser)
            .SingleOrDefaultAsync(item => item.CommunityReportId == reportId, cancellationToken);
    }

    public async Task<(List<AdminReviewResponse> Items, int TotalCount)> GetAdminReviewListAsync(
        string? keyword,
        string? normalizedStatus,
        string? normalizedTargetType,
        int? score,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
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

        return (items, totalCount);
    }

    public Task<RatingHistory?> GetRatingHistoryByIdAsync(int ratingId, CancellationToken cancellationToken = default)
    {
        return _dbContext.RatingHistories
            .Include(item => item.User)
            .Include(item => item.ModeratedByUser)
            .SingleOrDefaultAsync(item => item.RatingId == ratingId, cancellationToken);
    }

    public async Task<(List<AdminPostResponse> Items, int TotalCount)> GetAdminPostListAsync(
        string? keyword,
        bool? hiddenOnly,
        int? groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Posts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(post =>
                (post.Content != null && post.Content.Contains(keyword))
                || post.Author.Username.Contains(keyword)
                || post.Author.Email.Contains(keyword)
                || (post.Group != null && post.Group.GroupName.Contains(keyword)));
        }

        if (hiddenOnly is true)
            query = query.Where(post => post.IsHidden);

        if (groupId is not null)
            query = query.Where(post => post.GroupId == groupId);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(post => post.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(post => new AdminPostResponse
            {
                PostId = post.PostId,
                AuthorId = post.AuthorId,
                AuthorName = post.Author.Username,
                AuthorEmail = post.Author.Email,
                GroupId = post.GroupId,
                GroupName = post.Group != null ? post.Group.GroupName : null,
                Content = post.Content,
                PostType = post.PostType,
                Visibility = post.Visibility,
                IsHidden = post.IsHidden,
                ModerationNote = post.ModerationNote,
                ModeratedAt = post.ModeratedAt,
                ModeratedByName = post.ModeratedByUser != null ? post.ModeratedByUser.Username : null,
                LikeCount = post.PostLikes.Count,
                CommentCount = post.PostComments.Count,
                CreatedAt = post.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Post?> GetPostForModerationByIdAsync(int postId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Posts
            .Include(post => post.Author)
            .Include(post => post.Group)
            .Include(post => post.ModeratedByUser)
            .Include(post => post.PostComments)
            .Include(post => post.PostLikes)
            .Include(post => post.PostMedia)
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
    }

    public Task RemovePostAsync(Post post, CancellationToken cancellationToken = default)
    {
        _dbContext.Posts.Remove(post);
        return Task.CompletedTask;
    }

    public async Task<(List<AdminClubResponse> Items, int TotalCount)> GetAdminClubListAsync(
        string? keyword,
        bool? suspendedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SocialGroups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(group =>
                group.GroupName.Contains(keyword)
                || (group.Description != null && group.Description.Contains(keyword))
                || group.Owner.User.Username.Contains(keyword)
                || group.Owner.User.Email.Contains(keyword));
        }

        if (suspendedOnly is true)
            query = query.Where(group => group.IsSuspended);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(group => group.IsSuspended)
            .ThenByDescending(group => group.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(group => new AdminClubResponse
            {
                GroupId = group.GroupId,
                GroupName = group.GroupName,
                Description = group.Description,
                GroupType = group.GroupType,
                OwnerId = group.OwnerId,
                OwnerName = group.Owner.User.Username,
                MemberCount = group.GroupMembers.Count(member => member.Status == "Accepted"),
                PostCount = group.Posts.Count,
                IsSuspended = group.IsSuspended,
                SuspensionReason = group.SuspensionReason,
                ModeratedAt = group.ModeratedAt,
                ModeratedByName = group.ModeratedByUser != null ? group.ModeratedByUser.Username : null,
                CreatedAt = group.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<SocialGroup?> GetGroupForModerationByIdAsync(int groupId, CancellationToken cancellationToken = default)
    {
        return _dbContext.SocialGroups
            .Include(group => group.Owner).ThenInclude(owner => owner.User)
            .Include(group => group.ModeratedByUser)
            .Include(group => group.GroupMembers)
            .Include(group => group.Posts)
            .SingleOrDefaultAsync(group => group.GroupId == groupId, cancellationToken);
    }

    public Task<Booking?> GetBookingForCancelByIdAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Bookings
            .Include(booking => booking.Court).ThenInclude(court => court.Venue).ThenInclude(venue => venue.Owner).ThenInclude(owner => owner.User)
            .Include(booking => booking.Player).ThenInclude(player => player!.User)
            .Include(booking => booking.Payments)
            .Include(booking => booking.StatusHistories)
            .SingleOrDefaultAsync(booking => booking.BookingId == bookingId, cancellationToken);
    }

    private async Task<List<AdminDashboardActionItemResponse>> BuildActionItems(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var submittedVenues = await _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.ApprovalStatus == "Pending")
            .OrderByDescending(venue => venue.VenueAuditLogs
                .Where(log => log.Action == "OwnerSubmittedForApproval")
                .Select(log => (DateTime?)log.Timestamp)
                .Max())
            .Take(4)
            .Select(venue => new AdminDashboardActionItemResponse
            {
                Type = "VenueApproval",
                Title = venue.VenueName,
                Description = $"{venue.Owner.User.Username} - {venue.Courts.Count} sân con đang chờ duyệt.",
                Status = "Chờ duyệt sân",
                Tone = "warning",
                LinkTo = "/admin/courts",
                CreatedAt = venue.VenueAuditLogs
                    .Where(log => log.Action == "OwnerSubmittedForApproval")
                    .Select(log => (DateTime?)log.Timestamp)
                    .Max()
            })
            .ToListAsync(cancellationToken);

        var listingPayments = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(payment => payment.Status == "PendingReview")
            .OrderByDescending(payment => payment.SubmittedAt)
            .Take(4)
            .Select(payment => new AdminDashboardActionItemResponse
            {
                Type = "ListingPayment",
                Title = payment.Venue.VenueName,
                Description = $"{payment.Venue.Owner.User.Username} - biên lai phí lên sàn {payment.Amount:n0} VND.",
                Status = "Chờ duyệt biên lai",
                Tone = "info",
                LinkTo = "/admin/transactions",
                CreatedAt = payment.SubmittedAt
            })
            .ToListAsync(cancellationToken);

        var stalePayments = await _dbContext.Payments.AsNoTracking()
            .Where(payment => payment.Status == "WaitingForConfirmation"
                && payment.SubmittedAt != null
                && payment.SubmittedAt <= now.AddHours(-24))
            .OrderBy(payment => payment.SubmittedAt)
            .Take(4)
            .Select(payment => new AdminDashboardActionItemResponse
            {
                Type = "BookingPayment",
                Title = payment.Booking.BookingCode ?? $"Booking #{payment.BookingId}",
                Description = $"{payment.Payer.User.Username} - biên lai booking quá 24 giờ.",
                Status = "Cần hỗ trợ",
                Tone = "danger",
                LinkTo = "/admin/bookings",
                CreatedAt = payment.SubmittedAt
            })
            .ToListAsync(cancellationToken);

        return submittedVenues.Concat(listingPayments).Concat(stalePayments).ToList();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Serializable,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string RoleLabel(string role) => role switch
    {
        "Admin" => "Admin",
        "Player" => "Người chơi",
        "VenueOwner" => "Chủ sân",
        "Staff" => "Nhân viên",
        _ => "Chưa chọn vai trò"
    };
}
